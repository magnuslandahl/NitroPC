namespace NitroPC.Models;

public enum RamDisplayMode
{
    Percent,
    Gigabytes,
    Both
}

public enum CpuDisplayMode
{
    Cores,
    History
}

public enum KeepAwakeBarMode
{
    Badge,
    CompactIcon,
    Hidden
}

public enum QuietModeLevel
{
    Off,
    Auto,
    Quiet
}

public sealed record QuietModeRecoveryState(
    Guid SchemeId,
    uint AcProcessorBoostMode,
    uint DcProcessorBoostMode);

public sealed class NitroSettings
{
    /// <summary>0 means indefinite. The default session length is 60 minutes.</summary>
    public int KeepAwakeMinutes { get; set; } = 60;

    public bool AutoStartKeepAwakeOnLaunch { get; set; }

    public CpuDisplayMode CpuDisplayMode { get; set; } = CpuDisplayMode.History;

    public RamDisplayMode RamDisplayMode { get; set; } = RamDisplayMode.Percent;

    public KeepAwakeBarMode KeepAwakeBarMode { get; set; } = KeepAwakeBarMode.Badge;

    public bool AutomaticallyCheckForUpdates { get; set; } = true;

    public QuietModeLevel QuietMode { get; set; } = QuietModeLevel.Off;

    public int AutoQuietThresholdCelsius { get; set; } = 75;

    public QuietModeRecoveryState? QuietModeRecoveryState { get; set; }
}
