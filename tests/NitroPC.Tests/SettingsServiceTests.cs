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
            QuietMode = QuietModeLevel.Auto,
            AutoQuietThresholdCelsius = 80,
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
        Assert.Equal(expected.QuietMode, actual.QuietMode);
        Assert.Equal(expected.AutoQuietThresholdCelsius, actual.AutoQuietThresholdCelsius);
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
        Assert.Equal(60, actual.AutoQuietThresholdCelsius);
        Assert.True(actual.AutomaticallyCheckForUpdates);
    }

    [Fact]
    public void NormalizesLegacyValuesAndRecoversFromInterruptedTempSave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "NitroPC.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");

        // Primary file corrupted during reboot, while atomic .tmp write completed with legacy integer enums
        File.WriteAllText(path, "");
        File.WriteAllText(
            path + ".tmp",
            """
            {
              "KeepAwakeMinutes": 30,
              "CpuDisplayMode": 0,
              "RamDisplayMode": 1,
              "KeepAwakeBarMode": 2,
              "QuietMode": 1,
              "AutoQuietThresholdCelsius": 75
            }
            """);

        var actual = new SettingsService(path).Load();

        Assert.Equal(60, actual.KeepAwakeMinutes);
        Assert.Equal(CpuDisplayMode.Cores, actual.CpuDisplayMode);
        Assert.Equal(RamDisplayMode.Gigabytes, actual.RamDisplayMode);
        Assert.Equal(KeepAwakeBarMode.Hidden, actual.KeepAwakeBarMode);
        Assert.Equal(QuietModeLevel.Auto, actual.QuietMode);
        Assert.Equal(70, actual.AutoQuietThresholdCelsius);
    }
}
