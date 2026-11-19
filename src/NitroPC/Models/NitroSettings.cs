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
    public static IReadOnlyList<int> SupportedKeepAwakePresets { get; } = [60, 120, 240, 480, 0];

    public static IReadOnlyList<int> SupportedAutoQuietThresholds { get; } = [60, 70, 80];

    /// <summary>0 means indefinite. The default session length is 60 minutes.</summary>
    public int KeepAwakeMinutes { get; set; } = 60;

    public bool AutoStartKeepAwakeOnLaunch { get; set; }

    public CpuDisplayMode CpuDisplayMode { get; set; } = CpuDisplayMode.History;

    public RamDisplayMode RamDisplayMode { get; set; } = RamDisplayMode.Percent;

    public KeepAwakeBarMode KeepAwakeBarMode { get; set; } = KeepAwakeBarMode.Badge;

    public bool AutomaticallyCheckForUpdates { get; set; } = true;

    public QuietModeLevel QuietMode { get; set; } = QuietModeLevel.Off;

    public int AutoQuietThresholdCelsius { get; set; } = 60;

    public QuietModeRecoveryState? QuietModeRecoveryState { get; set; }

    public static int NormalizePresetMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return 0;
        }

        return SupportedKeepAwakePresets
            .Where(preset => preset > 0)
            .MinBy(preset => Math.Abs(preset - minutes));
    }

    public static int NormalizeAutoQuietThreshold(int thresholdCelsius)
    {
        if (thresholdCelsius <= 0)
        {
            return 60;
        }

        return SupportedAutoQuietThresholds
            .MinBy(value => Math.Abs(value - thresholdCelsius));
    }

    public void Normalize()
    {
        KeepAwakeMinutes = NormalizePresetMinutes(KeepAwakeMinutes);
        AutoQuietThresholdCelsius = NormalizeAutoQuietThreshold(AutoQuietThresholdCelsius);

        if (!Enum.IsDefined(CpuDisplayMode))
        {
            CpuDisplayMode = CpuDisplayMode.History;
        }

        if (!Enum.IsDefined(RamDisplayMode))
        {
            RamDisplayMode = RamDisplayMode.Percent;
        }

        if (!Enum.IsDefined(KeepAwakeBarMode))
        {
            KeepAwakeBarMode = KeepAwakeBarMode.Badge;
        }

        if (!Enum.IsDefined(QuietMode))
        {
            QuietMode = QuietModeLevel.Off;
        }
    }
}
