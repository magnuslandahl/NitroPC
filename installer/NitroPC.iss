#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef AppArch
  #define AppArch "x64"
#endif
#ifndef SourceDir
  #error "SourceDir must be passed via /DSourceDir=..."
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef OutputBaseName
  #define OutputBaseName "NitroPC-win-" + AppArch + "-Setup"
#endif

#if VER >= EncodeVer(6,3,0,0)
  #define X64Arch "x64compatible"
#else
  #define X64Arch "x64"
#endif

[Setup]
AppId=NitroPC
AppName=NitroPC
AppVersion={#AppVersion}
AppVerName=NitroPC {#AppVersion}
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=NitroPC Windows 11 Taskbar Toolbox Setup
AppPublisher=Magnus Landahl
AppPublisherURL=https://github.com/magnuslandahl/NitroPC
AppSupportURL=https://github.com/magnuslandahl/NitroPC/issues
AppUpdatesURL=https://github.com/magnuslandahl/NitroPC/releases/latest
DefaultDirName={autopf}\NitroPC
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=no
RestartApplications=no
WizardStyle=modern
SetupIconFile=..\src\NitroPC\Assets\NitroPC.ico
UninstallDisplayIcon={app}\NitroPC.exe
UninstallDisplayName=NitroPC
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
#if AppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed={#X64Arch}
ArchitecturesInstallIn64BitMode={#X64Arch}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start NitroPC automatically when Windows starts"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\NitroPC"; Filename: "{app}\NitroPC.exe"; WorkingDir: "{app}"; Comment: "Windows 11 taskbar telemetry, Keep Awake, and Quiet Mode"
Name: "{autodesktop}\NitroPC"; Filename: "{app}\NitroPC.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NitroPC"; ValueData: """{app}\NitroPC.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\NitroPC.exe"; Description: "Launch NitroPC"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  NitroMutexName = 'Local\NitroPC_SingleInstance_Mutex';
  WindowsRunSubkey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure StopRunningNitroPC();
var
  ResultCode: Integer;
  Attempt: Integer;
begin
  if not CheckForMutexes(NitroMutexName) then
    Exit;

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM NitroPC.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  for Attempt := 1 to 20 do
  begin
    if not CheckForMutexes(NitroMutexName) then
      Exit;
    Sleep(150);
  end;

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM NitroPC.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  StopRunningNitroPC();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningNitroPC();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  CurrentValue: String;
  ExpectedQuoted: String;
  ExpectedPlain: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    ExpectedQuoted := '"' + ExpandConstant('{app}\NitroPC.exe') + '"';
    ExpectedPlain := ExpandConstant('{app}\NitroPC.exe');
    if RegQueryStringValue(HKCU, WindowsRunSubkey, 'NitroPC', CurrentValue) then
    begin
      if (CompareText(CurrentValue, ExpectedQuoted) = 0) or
         (CompareText(CurrentValue, ExpectedPlain) = 0) then
      begin
        RegDeleteValue(HKCU, WindowsRunSubkey, 'NitroPC');
      end;
    end;
  end;
end;
