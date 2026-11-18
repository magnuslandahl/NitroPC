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

public sealed class NitroSettings
{
    /// <summary>0 means indefinite. The default session length is 60 minutes.</summary>
    public int KeepAwakeMinutes { get; set; } = 60;

    public bool AutoStartKeepAwakeOnLaunch { get; set; }

    public CpuDisplayMode CpuDisplayMode { get; set; } = CpuDisplayMode.Cores;

    public RamDisplayMode RamDisplayMode { get; set; } = RamDisplayMode.Percent;

    public KeepAwakeBarMode KeepAwakeBarMode { get; set; } = KeepAwakeBarMode.Badge;

    public bool AutomaticallyCheckForUpdates { get; set; } = true;
}
