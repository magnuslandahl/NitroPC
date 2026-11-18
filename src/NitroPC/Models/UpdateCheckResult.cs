namespace NitroPC.Models;

public sealed record UpdateCheckResult(
    bool Checked,
    bool Available,
    bool Installable,
    Version? Version,
    string Message,
    string ReleasePageUrl,
    string? AssetName = null,
    long? AssetSize = null)
{
    internal long? ReleaseId { get; init; }

    internal long? AssetId { get; init; }

    internal long? ChecksumAssetId { get; init; }

    internal string? ExpectedSha256 { get; init; }

    internal static UpdateCheckResult Failure(string message, string releasePageUrl) =>
        new(false, false, false, null, message, releasePageUrl);
}
