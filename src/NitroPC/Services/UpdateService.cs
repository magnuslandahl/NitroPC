using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NitroPC.Models;

namespace NitroPC.Services;

public sealed class UpdateService : IDisposable
{
    public const string Repository = "magnuslandahl/NitroPC";

    private const string LatestReleaseApi =
        $"https://api.github.com/repos/{Repository}/releases/latest";

    private const string ReleasePage = $"https://github.com/{Repository}/releases/latest";
    private const int MaximumManifestBytes = 1024 * 1024;
    private const long MaximumPackageBytes = 500L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;
    private UpdateCheckResult? _pendingUpdate;

    public UpdateService()
        : this(CreateHttpClient(), typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0))
    {
    }

    internal UpdateService(HttpClient httpClient, Version currentVersion)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _currentVersion = currentVersion;
        ConfigureHttpClient(_httpClient);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        _pendingUpdate = null;
        try
        {
            using var response = await GetAsync(LatestReleaseApi, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var release = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = await DescribeReleaseAsync(release.RootElement, cancellationToken);
            if (result.Installable)
            {
                _pendingUpdate = result;
            }

            return result;
        }
        catch (HttpRequestException exception)
        {
            return UpdateCheckResult.Failure(
                $"Could not check for updates: {exception.Message}",
                ReleasePage);
        }
        catch (TaskCanceledException exception)
        {
            return UpdateCheckResult.Failure(
                $"Could not check for updates: {exception.Message}",
                ReleasePage);
        }
        catch (JsonException exception)
        {
            return UpdateCheckResult.Failure(
                $"The update service returned unreadable release data: {exception.Message}",
                ReleasePage);
        }
        catch (InvalidDataException exception)
        {
            return UpdateCheckResult.Failure(
                $"The latest release cannot be trusted: {exception.Message}",
                ReleasePage);
        }
        catch (InvalidOperationException exception)
        {
            return UpdateCheckResult.Failure(
                $"The latest release cannot be installed: {exception.Message}",
                ReleasePage);
        }
    }

    public async Task DownloadAndLaunchUpdateAsync(
        UpdateCheckResult selected,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSelection(selected);

        var fresh = await CheckAsync(cancellationToken);
        if (!fresh.Installable ||
            fresh.ReleaseId != selected.ReleaseId ||
            fresh.AssetId != selected.AssetId ||
            fresh.ChecksumAssetId != selected.ChecksumAssetId ||
            fresh.AssetName != selected.AssetName ||
            fresh.AssetSize != selected.AssetSize ||
            fresh.ExpectedSha256 != selected.ExpectedSha256)
        {
            throw new InvalidOperationException("The selected release changed. Check for updates again.");
        }

        var updatesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NitroPC",
            "Updates");
        var updateRoot = Path.Combine(updatesRoot, selected.Version!.ToString());
        if (Directory.Exists(updateRoot))
        {
            Directory.Delete(updateRoot, recursive: true);
        }

        Directory.CreateDirectory(updateRoot);
        var packagePath = Path.Combine(updateRoot, selected.AssetName!);
        var packagePartPath = packagePath + ".part";

        try
        {
            await DownloadVerifiedAsync(
                AssetApiUrl(selected.AssetId!.Value),
                packagePartPath,
                selected.AssetSize!.Value,
                selected.ExpectedSha256!,
                progress,
                cancellationToken);
            File.Move(packagePartPath, packagePath, overwrite: true);

            var stagedPath = Path.Combine(updateRoot, "staged");
            ExtractPackage(packagePath, stagedPath);
            File.Delete(packagePath);
            LaunchUpdater(stagedPath);
        }
        finally
        {
            DeleteIfExists(packagePartPath);
        }
    }

    internal static bool IsNewer(Version candidate, Version current) => candidate > current;

    internal static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Version.TryParse(text.Trim().TrimStart('v', 'V'), out var version)
            ? version
            : null;
    }

    internal static string? FindChecksum(string manifest, string assetName)
    {
        foreach (var rawLine in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf(' ', StringComparison.Ordinal);
            if (separator != 64)
            {
                continue;
            }

            var hash = line[..64];
            var fileName = line[64..].TrimStart().TrimStart('*');
            if (string.Equals(fileName, assetName, StringComparison.Ordinal) &&
                hash.All(uri => (uri >= '0' && uri <= '9') || (uri >= 'a' && uri <= 'f')))
            {
                return hash;
            }
        }

        return null;
    }

    internal static string AssetNameForCurrentArchitecture()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "NitroPC-win-x64.zip",
            Architecture.Arm64 => "NitroPC-win-arm64.zip",
            _ => throw new PlatformNotSupportedException(
                $"NitroPC updates do not support {RuntimeInformation.OSArchitecture}.")
        };
    }

    private async Task<UpdateCheckResult> DescribeReleaseAsync(
        JsonElement release,
        CancellationToken cancellationToken)
    {
        var releaseId = GetPositiveId(release, "id");
        var version = ParseVersion(GetString(release, "tag_name"));
        var pageUrl = GetString(release, "html_url") ?? ReleasePage;
        if (releaseId is null || version is null || !release.TryGetProperty("assets", out var assetsElement))
        {
            return new UpdateCheckResult(
                true,
                false,
                false,
                null,
                "The latest release has no usable version information.",
                pageUrl);
        }

        if (!IsNewer(version, _currentVersion))
        {
            return new UpdateCheckResult(
                true,
                false,
                false,
                version,
                $"NitroPC {_currentVersion.ToString(3)} is up to date.",
                pageUrl)
            {
                ReleaseId = releaseId
            };
        }

        var expectedAssetName = AssetNameForCurrentArchitecture();
        JsonElement? installer = null;
        JsonElement? checksum = null;
        var checksumCount = 0;
        foreach (var asset in assetsElement.EnumerateArray())
        {
            var name = GetString(asset, "name");
            if (string.Equals(name, expectedAssetName, StringComparison.Ordinal))
            {
                installer = asset;
            }
            else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.Ordinal))
            {
                checksumCount++;
                checksum = asset;
            }
        }

        if (installer is null)
        {
            return new UpdateCheckResult(
                true,
                true,
                false,
                version,
                $"NitroPC {version} is available, but no {expectedAssetName} download is attached.",
                pageUrl)
            {
                ReleaseId = releaseId
            };
        }

        var assetId = GetPositiveId(installer.Value, "id");
        var assetSize = GetPositiveLong(installer.Value, "size");
        var checksumId = checksum is null ? null : GetPositiveId(checksum.Value, "id");
        if (assetId is null || assetSize is null || assetSize <= 0 ||
            checksumId is null || checksumCount != 1)
        {
            return new UpdateCheckResult(
                true,
                true,
                false,
                version,
                "This release cannot be installed because its download or checksum identity is missing.",
                pageUrl)
            {
                ReleaseId = releaseId
            };
        }

        var expectedHash = await DownloadChecksumAsync(checksumId.Value, expectedAssetName, cancellationToken);
        return new UpdateCheckResult(
            true,
            true,
            true,
            version,
            $"NitroPC {version} is available.",
            pageUrl,
            expectedAssetName,
            assetSize)
        {
            ReleaseId = releaseId,
            AssetId = assetId,
            ChecksumAssetId = checksumId,
            ExpectedSha256 = expectedHash
        };
    }

    private async Task<string> DownloadChecksumAsync(
        long checksumAssetId,
        string expectedAssetName,
        CancellationToken cancellationToken)
    {
        using var response = await GetAsync(AssetApiUrl(checksumAssetId), cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await ReadLimitedAsync(
            response.Content,
            MaximumManifestBytes,
            cancellationToken);
        var hash = FindChecksum(Encoding.UTF8.GetString(bytes), expectedAssetName);
        return hash ?? throw new InvalidOperationException(
            $"SHA256SUMS.txt does not contain an exact entry for {expectedAssetName}.");
    }

    private async Task DownloadVerifiedAsync(
        Uri uri,
        string targetPath,
        long expectedSize,
        string expectedSha256,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var actualLength = response.Content.Headers.ContentLength;
        if (actualLength is > MaximumPackageBytes || expectedSize > MaximumPackageBytes ||
            (actualLength is not null && actualLength != expectedSize))
        {
            throw new InvalidDataException("The update download size does not match the release manifest.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long received = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            received += count;
            if (received > MaximumPackageBytes || received > expectedSize)
            {
                throw new InvalidDataException("The update download exceeds its declared size.");
            }

            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            progress?.Report(expectedSize == 0 ? 0 : Math.Min(1, (double)received / expectedSize));
        }

        if (received != expectedSize)
        {
            throw new InvalidDataException("The update download stopped before all bytes arrived.");
        }

        var actualHash = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actualHash, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update download failed SHA-256 verification.");
        }
    }

    internal static void ExtractPackage(string packagePath, string stagedPath)
    {
        Directory.CreateDirectory(stagedPath);
        var root = Path.GetFullPath(stagedPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        long totalExpandedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update package contains an unsafe path.");
            }

            totalExpandedBytes += Math.Max(0, entry.Length);
            if (totalExpandedBytes > MaximumPackageBytes)
            {
                throw new InvalidDataException("The expanded update package is unexpectedly large.");
            }

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }

        if (!File.Exists(Path.Combine(stagedPath, "NitroPC.exe")) ||
            !File.Exists(Path.Combine(stagedPath, "NitroPC.Updater.exe")))
        {
            throw new InvalidDataException("The update package is missing NitroPC or its updater.");
        }
    }

    private static void LaunchUpdater(string stagedPath)
    {
        var updaterSource = Path.Combine(AppContext.BaseDirectory, "NitroPC.Updater.exe");
        if (!File.Exists(updaterSource))
        {
            throw new FileNotFoundException("NitroPC.Updater.exe is missing.", updaterSource);
        }

        var updaterRunDirectory = Path.Combine(
            Path.GetTempPath(),
            "NitroPC-Update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updaterRunDirectory);
        foreach (var extension in new[] { ".exe", ".dll", ".deps.json", ".runtimeconfig.json" })
        {
            var source = Path.Combine(
                AppContext.BaseDirectory,
                "NitroPC.Updater" + extension);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("A required updater component is missing.", source);
            }

            File.Copy(source, Path.Combine(updaterRunDirectory, Path.GetFileName(source)));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(updaterRunDirectory, "NitroPC.Updater.exe"),
            WorkingDirectory = updaterRunDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(stagedPath);
        startInfo.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        startInfo.ArgumentList.Add("NitroPC.exe");
        startInfo.ArgumentList.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NitroPC",
            "update.log"));

        Process.Start(startInfo);
    }

    private void ValidateSelection(UpdateCheckResult selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (_pendingUpdate is null ||
            selected != _pendingUpdate ||
            !selected.Installable ||
            selected.ReleaseId is null ||
            selected.AssetId is null ||
            selected.ChecksumAssetId is null ||
            selected.AssetSize is not > 0 ||
            selected.Version is null ||
            string.IsNullOrWhiteSpace(selected.AssetName) ||
            string.IsNullOrWhiteSpace(selected.ExpectedSha256) ||
            selected.ExpectedSha256.Length != 64)
        {
            throw new InvalidOperationException("The selected update is no longer valid. Check for updates again.");
        }
    }

    private async Task<HttpResponseMessage> GetAsync(
        string uri,
        CancellationToken cancellationToken) =>
        await GetAsync(new Uri(uri), cancellationToken);

    private async Task<HttpResponseMessage> GetAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("SHA256SUMS.txt is unexpectedly large.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + count > maximumBytes)
            {
                throw new InvalidDataException("SHA256SUMS.txt is unexpectedly large.");
            }

            await memory.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        return memory.ToArray();
    }

    private static HttpClient CreateHttpClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static void ConfigureHttpClient(HttpClient client)
    {
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NitroPC");
        }

        client.DefaultRequestHeaders.AcceptEncoding.Clear();
    }

    private static Uri AssetApiUrl(long id) =>
        new($"https://api.github.com/repos/{Repository}/releases/assets/{id}");

    private static long? GetPositiveId(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.TryGetInt64(out var id) &&
               id > 0
            ? id
            : null;
    }

    private static long? GetPositiveLong(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.TryGetInt64(out var id) &&
               id >= 0
            ? id
            : null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
