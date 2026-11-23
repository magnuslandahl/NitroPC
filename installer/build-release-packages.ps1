param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputDir = "artifacts",

    [string[]]$Rids = @("win-x64", "win-arm64")
)

$ErrorActionPreference = "Stop"

$cleanVersion = $Version.Trim().TrimStart("v", "V")
if ($cleanVersion -notmatch "^\d+\.\d+\.\d+$") {
    throw "Invalid semantic version '$Version'. Expected X.Y.Z."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$issPath = Join-Path $PSScriptRoot "NitroPC.iss"
if (-not (Test-Path $issPath)) {
    throw "Missing Inno Setup script at '$issPath'."
}

function Resolve-IsccPath {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    if ($env:GITHUB_ACTIONS -eq "true") {
        Write-Host "Installing Inno Setup via Chocolatey..."
        & choco install innosetup --no-progress -y
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to install Inno Setup via Chocolatey."
        }
        foreach ($candidate in $candidates) {
            if ($candidate -and (Test-Path $candidate)) {
                return $candidate
            }
        }
    }

    throw "Inno Setup 6 (ISCC.exe) was not found. Install JRSoftware.InnoSetup."
}

$iscc = Resolve-IsccPath
Write-Host "Using ISCC: $iscc"

$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
} else {
    Join-Path $repoRoot $OutputDir
}

if (Test-Path $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("NitroPC-Package-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

try {
    Push-Location $repoRoot
    try {
        foreach ($rid in $Rids) {
            $arch = switch ($rid) {
                "win-x64"   { "x64" }
                "win-arm64" { "arm64" }
                default     { throw "Unsupported RID '$rid'." }
            }

            $appStage = Join-Path $stagingRoot "app-$rid"
            $updaterStage = Join-Path $stagingRoot "updater-$rid"

            Write-Host "Publishing standalone NitroPC.Updater ($rid)..."
            & dotnet publish "src\NitroPC.Updater\NitroPC.Updater.csproj" `
                -c Release `
                -r $rid `
                --self-contained true `
                -p:PublishSingleFile=true `
                -p:IncludeNativeLibrariesForSelfExtract=true `
                -p:EnableCompressionInSingleFile=true `
                -p:DebugType=none `
                -p:DebugSymbols=false `
                -p:Version=$cleanVersion `
                -o $updaterStage `
                --nologo
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet publish failed for NitroPC.Updater ($rid)."
            }

            Write-Host "Publishing self-contained NitroPC ($rid)..."
            & dotnet publish "src\NitroPC\NitroPC.csproj" `
                -c Release `
                -r $rid `
                --self-contained true `
                -p:DebugType=none `
                -p:DebugSymbols=false `
                -p:Version=$cleanVersion `
                -o $appStage `
                --nologo
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet publish failed for NitroPC ($rid)."
            }

            # Replace any build-copied multi-file updater artifacts with the standalone single-file updater
            foreach ($staleExt in @(".dll", ".deps.json", ".runtimeconfig.json", ".pdb")) {
                $stalePath = Join-Path $appStage ("NitroPC.Updater" + $staleExt)
                if (Test-Path $stalePath) {
                    Remove-Item -LiteralPath $stalePath -Force
                }
            }
            Copy-Item -LiteralPath (Join-Path $updaterStage "NitroPC.Updater.exe") -Destination (Join-Path $appStage "NitroPC.Updater.exe") -Force

            if (-not (Test-Path (Join-Path $appStage "NitroPC.exe")) -or -not (Test-Path (Join-Path $appStage "NitroPC.Updater.exe"))) {
                throw "Missing NitroPC.exe or NitroPC.Updater.exe in '$appStage'."
            }

            $stampedVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $appStage "NitroPC.dll")).Version
            if ($stampedVersion -ne [Version]::Parse("$cleanVersion.0")) {
                throw "Expected assembly version $cleanVersion.0 in NitroPC.dll, got $stampedVersion."
            }

            $zipName = "NitroPC-$rid.zip"
            $zipPath = Join-Path $resolvedOutput $zipName
            Write-Host "Creating portable/update archive $zipName..."
            Compress-Archive -Path (Join-Path $appStage "*") -DestinationPath $zipPath -Force

            $setupBaseName = "NitroPC-$rid-Setup"
            Write-Host "Building installer $setupBaseName.exe..."
            & $iscc `
                "/Qp" `
                "/DAppVersion=$cleanVersion" `
                "/DAppArch=$arch" `
                "/DSourceDir=$appStage" `
                "/DOutputDir=$resolvedOutput" `
                "/DOutputBaseName=$setupBaseName" `
                $issPath
            if ($LASTEXITCODE -ne 0) {
                throw "Inno Setup failed for $rid."
            }

            $setupPath = Join-Path $resolvedOutput "$setupBaseName.exe"
            if (-not (Test-Path $setupPath)) {
                throw "Expected installer '$setupPath' was not produced."
            }
        }
    }
    finally {
        Pop-Location
    }

    $releaseFiles = Get-ChildItem -LiteralPath $resolvedOutput -File |
        Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
        Sort-Object Name
    $sumLines = foreach ($file in $releaseFiles) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($file.Name)"
    }

    [System.IO.File]::WriteAllText(
        (Join-Path $resolvedOutput "SHA256SUMS.txt"),
        ($sumLines -join "`n") + "`n",
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "Release artifacts ready in ${resolvedOutput}:"
    Get-ChildItem -LiteralPath $resolvedOutput | Select-Object Name, @{Name="SizeMB";Expression={[Math]::Round($_.Length / 1MB, 2)}} | Format-Table -AutoSize
}
finally {
    if (Test-Path $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
