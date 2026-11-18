using NitroPC.Models;
using NitroPC.Services;

namespace NitroPC.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void RoundTripsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "NitroPC.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var service = new SettingsService(path);
        var expected = new NitroSettings
        {
            KeepAwakeMinutes = 120,
            CpuDisplayMode = CpuDisplayMode.Cores,
            RamDisplayMode = RamDisplayMode.Both,
            KeepAwakeBarMode = KeepAwakeBarMode.CompactIcon,
            AutoStartKeepAwakeOnLaunch = true,
            AutomaticallyCheckForUpdates = false,
            QuietModeRecoveryState = new QuietModeRecoveryState(Guid.NewGuid(), 2, 4)
        };

        service.Save(expected);
        var actual = service.Load();

        Assert.Equal(expected.KeepAwakeMinutes, actual.KeepAwakeMinutes);
        Assert.Equal(expected.CpuDisplayMode, actual.CpuDisplayMode);
        Assert.Equal(expected.RamDisplayMode, actual.RamDisplayMode);
        Assert.Equal(expected.KeepAwakeBarMode, actual.KeepAwakeBarMode);
        Assert.Equal(expected.AutoStartKeepAwakeOnLaunch, actual.AutoStartKeepAwakeOnLaunch);
        Assert.Equal(expected.AutomaticallyCheckForUpdates, actual.AutomaticallyCheckForUpdates);
        Assert.Equal(expected.QuietModeRecoveryState, actual.QuietModeRecoveryState);
    }

    [Fact]
    public void CorruptJsonReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "NitroPC.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not valid json");

        var actual = new SettingsService(path).Load();

        Assert.Equal(60, actual.KeepAwakeMinutes);
        Assert.Equal(CpuDisplayMode.History, actual.CpuDisplayMode);
        Assert.Equal(RamDisplayMode.Percent, actual.RamDisplayMode);
        Assert.Equal(KeepAwakeBarMode.Badge, actual.KeepAwakeBarMode);
        Assert.True(actual.AutomaticallyCheckForUpdates);
    }
}
