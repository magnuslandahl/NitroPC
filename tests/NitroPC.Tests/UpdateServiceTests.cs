using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using NitroPC.Services;

namespace NitroPC.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public void VersionsCompareNumerically()
    {
        Assert.True(UpdateService.IsNewer(new Version(0, 10, 0), new Version(0, 9, 9)));
        Assert.False(UpdateService.IsNewer(new Version(1, 0, 0), new Version(1, 0, 0)));
        Assert.False(UpdateService.IsNewer(new Version(0, 2, 0), new Version(0, 2, 0, 0)));
        Assert.True(UpdateService.IsNewer(new Version(0, 2, 1), new Version(0, 2, 0, 0)));
        Assert.Equal(new Version(2, 3, 4), UpdateService.ParseVersion("v2.3.4"));
        Assert.Equal(new Version(2, 3, 4), UpdateService.ParseVersion("NitroPC 2.3.4"));
    }

    [Fact]
    public void ChecksumMatchesExactAssetName()
    {
        var manifest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  Other.zip\n" +
                       "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  ./NitroPC-win-x64.zip\n";

        Assert.Equal(
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            UpdateService.FindChecksum(manifest, "NitroPC-win-x64.zip"));
        Assert.Null(UpdateService.FindChecksum(manifest, "NitroPC-win-arm64.zip"));
    }

    [Fact]
    public async Task NewReleaseBindsImmutableAssetAndChecksum()
    {
        var installerName = UpdateService.AssetNameForCurrentArchitecture();
        var checksum = new string('a', 64);
        var latest = $$"""
            {
              "id": 101,
              "tag_name": "v0.2.0",
              "html_url": "https://github.com/magnuslandahl/NitroPC/releases/tag/v0.2.0",
              "assets": [
                { "id": 201, "name": "{{installerName}}", "size": 1234 },
                { "id": 202, "name": "SHA256SUMS.txt", "size": 90 }
              ]
            }
            """;
        using var client = new HttpClient(new ReleaseHandler(latest, $"{checksum}  {installerName}\n"));
        using var service = new UpdateService(client, new Version(0, 1, 0));

        var result = await service.CheckAsync();

        Assert.True(result.Checked);
        Assert.True(result.Available);
        Assert.True(result.Installable);
        Assert.Equal(new Version(0, 2, 0), result.Version);
        Assert.Equal(installerName, result.AssetName);
        Assert.Equal(201, result.AssetId);
        Assert.Equal(202, result.ChecksumAssetId);
        Assert.Equal(checksum, result.ExpectedSha256);
    }

    [Fact]
    public async Task ReleaseWithoutChecksumIsNotInstallable()
    {
        var installerName = UpdateService.AssetNameForCurrentArchitecture();
        var latest = $$"""
            {
              "id": 101,
              "tag_name": "v0.2.0",
              "html_url": "https://github.com/magnuslandahl/NitroPC/releases/tag/v0.2.0",
              "assets": [
                { "id": 201, "name": "{{installerName}}", "size": 1234 }
              ]
            }
            """;
        using var client = new HttpClient(new ReleaseHandler(latest, ""));
        using var service = new UpdateService(client, new Version(0, 1, 0));

        var result = await service.CheckAsync();

        Assert.True(result.Available);
        Assert.False(result.Installable);
        Assert.Contains("checksum", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZipEntryCannotEscapeStagingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "NitroPC.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "update.zip");
        var stagingPath = Path.Combine(root, "staged");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("escape");
        }

        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ExtractPackage(archivePath, stagingPath));
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        private readonly string _latest;
        private readonly string _checksum;

        public ReleaseHandler(string latest, string checksum)
        {
            _latest = latest;
            _checksum = checksum;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var acceptMediaTypes = request.Headers.Accept
                .Select(header => header.MediaType ?? string.Empty)
                .ToArray();
            var isLatestEndpoint = request.RequestUri!.AbsolutePath.EndsWith(
                "/releases/latest",
                StringComparison.Ordinal);

            if (isLatestEndpoint)
            {
                Assert.Equal(["application/vnd.github+json"], acceptMediaTypes);
            }
            else
            {
                // Real GitHub asset endpoints return JSON metadata instead of binary bytes
                // if application/vnd.github+json is sent alongside application/octet-stream.
                Assert.Equal(["application/octet-stream"], acceptMediaTypes);
            }

            var body = isLatestEndpoint ? _latest : _checksum;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8)
            });
        }
    }
}
