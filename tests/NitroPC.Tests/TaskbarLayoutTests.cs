using NitroPC.Controls;
using NitroPC.Helpers;
using NitroPC.Interop;
using NitroPC.Models;
using NitroPC.Services;
using NitroPC.Views;

namespace NitroPC.Tests;

public sealed class TaskbarLayoutTests
{
    [Fact]
    public void PlacesStripImmediatelyBeforeTrayWhenClear()
    {
        var x = TaskbarLayout.FindRightAnchoredSlot(
            Rect(0, 100, 1000, 148),
            Rect(900, 100, 1000, 148),
            [Rect(0, 100, 500, 148)],
            width: 260,
            margin: 6);

        Assert.Equal(634, x);
    }

    [Fact]
    public void StepsLeftOfWeatherWidgetWithoutOverlappingTaskbarIcons()
    {
        var x = TaskbarLayout.FindRightAnchoredSlot(
            Rect(0, 2088, 3840, 2160),
            Rect(3432, 2088, 3840, 2160),
            [
                Rect(0, 2088, 485, 2160),
                Rect(485, 2088, 1739, 2160),
                Rect(3186, 2088, 3414, 2160),
                Rect(3431, 2088, 3479, 2160)
            ],
            width: 488,
            margin: 9);

        Assert.Equal(2689, x);
    }

    [Fact]
    public void ReturnsNullWhenIconsLeaveNoSpaceBeforeWeather()
    {
        var x = TaskbarLayout.FindRightAnchoredSlot(
            Rect(0, 2088, 3840, 2160),
            Rect(3432, 2088, 3840, 2160),
            [
                Rect(0, 2088, 2800, 2160),
                Rect(3186, 2088, 3414, 2160)
            ],
            width: 488,
            margin: 9);

        Assert.Null(x);
    }

    [Fact]
    public void NormalizesKeepAwakePresetsToSupportedHourButtons()
    {
        Assert.Equal(0, ToolboxFlyoutWindow.NormalizePresetMinutes(0));
        Assert.Equal(60, ToolboxFlyoutWindow.NormalizePresetMinutes(30));
        Assert.Equal(60, ToolboxFlyoutWindow.NormalizePresetMinutes(60));
        Assert.Equal(240, ToolboxFlyoutWindow.NormalizePresetMinutes(240));
        Assert.Equal(480, ToolboxFlyoutWindow.NormalizePresetMinutes(480));

        Assert.Equal(60, ToolboxFlyoutWindow.NormalizeAutoThreshold(0));
        Assert.Equal(60, ToolboxFlyoutWindow.NormalizeAutoThreshold(60));
        Assert.Equal(70, ToolboxFlyoutWindow.NormalizeAutoThreshold(70));
        Assert.Equal(70, ToolboxFlyoutWindow.NormalizeAutoThreshold(75));
        Assert.Equal(80, ToolboxFlyoutWindow.NormalizeAutoThreshold(80));
    }

    [Fact]
    public void DynamicRamWindowZoomsSmallChangesIntoVisibleRange()
    {
        var (min, max) = RamSparklineControl.ComputeDynamicWindow([61.1, 61.4, 61.8], 0, 3);

        Assert.InRange(max - min, 2.4, 3.0);
        Assert.True(min < 61.1);
        Assert.True(max > 61.8);
    }

    [Fact]
    public void DesktopAndShellOverlaysDoNotTriggerFullscreenHide()
    {
        var primaryMonitor = Rect(0, 0, 3840, 2160);

        // Dual-monitor Desktop (Progman)
        Assert.False(FullscreenDetector.ShouldHideTaskbarStrip(
            "Progman",
            "explorer",
            isVisible: true,
            isCloaked: false,
            Rect(-3840, 0, 3840, 2160),
            primaryMonitor));

        // Start Menu / Search / Shell overlays
        Assert.False(FullscreenDetector.ShouldHideTaskbarStrip(
            "Windows.UI.Core.CoreWindow",
            "StartMenuExperienceHost",
            isVisible: true,
            isCloaked: false,
            primaryMonitor,
            primaryMonitor));
        Assert.False(FullscreenDetector.ShouldHideTaskbarStrip(
            "Windows.UI.Core.CoreWindow",
            "SearchHost",
            isVisible: true,
            isCloaked: false,
            primaryMonitor,
            primaryMonitor));
        Assert.False(FullscreenDetector.ShouldHideTaskbarStrip(
            "XamlExplorerHostIslandWindow",
            "explorer",
            isVisible: true,
            isCloaked: false,
            primaryMonitor,
            primaryMonitor));

        // Cloaked UWP window
        Assert.False(FullscreenDetector.ShouldHideTaskbarStrip(
            "ApplicationFrameWindow",
            "ApplicationFrameHost",
            isVisible: true,
            isCloaked: true,
            primaryMonitor,
            primaryMonitor));

        // Fullscreen app on secondary monitor does not hide primary monitor strip
        Assert.False(FullscreenDetector.ShouldHideTaskbarStrip(
            "Chrome_WidgetWin_1",
            "msedge",
            isVisible: true,
            isCloaked: false,
            Rect(-3840, 0, 0, 2160),
            primaryMonitor));
    }

    [Fact]
    public void RealFullscreenAppOnSameMonitorTriggersHide()
    {
        var primaryMonitor = Rect(0, 0, 3840, 2160);

        Assert.True(FullscreenDetector.ShouldHideTaskbarStrip(
            "Chrome_WidgetWin_1",
            "msedge",
            isVisible: true,
            isCloaked: false,
            primaryMonitor,
            primaryMonitor));

        // File Explorer in F11 fullscreen mode
        Assert.True(FullscreenDetector.ShouldHideTaskbarStrip(
            "CabinetWClass",
            "explorer",
            isVisible: true,
            isCloaked: false,
            primaryMonitor,
            primaryMonitor));
    }

    [Fact]
    public void SelectResponsiveSlotStepsDownThroughCompactModesBeforeHiding()
    {
        var shell = Rect(0, 2088, 3840, 2160);
        var tray = Rect(3432, 2088, 3840, 2160);
        var weather = Rect(3186, 2088, 3414, 2160);
        var candidates = new[]
        {
            new TaskbarDensityCandidate(TaskbarStripDensity.Full, 552),
            new TaskbarDensityCandidate(TaskbarStripDensity.CompactGraphs, 360),
            new TaskbarDensityCandidate(TaskbarStripDensity.ReadoutsOnly, 210),
            new TaskbarDensityCandidate(TaskbarStripDensity.Mini, 117),
            new TaskbarDensityCandidate(TaskbarStripDensity.IconOnly, 54)
        };

        // Plenty of room -> Full
        var full = TaskbarLayout.SelectResponsiveSlot(
            shell, tray, [Rect(0, 2088, 2500, 2160), weather], candidates, margin: 9);
        Assert.NotNull(full);
        Assert.Equal(TaskbarStripDensity.Full, full.Value.Density);

        // 420px gap -> CompactGraphs
        var compactGraphs = TaskbarLayout.SelectResponsiveSlot(
            shell, tray, [Rect(0, 2088, 2766, 2160), weather], candidates, margin: 9);
        Assert.NotNull(compactGraphs);
        Assert.Equal(TaskbarStripDensity.CompactGraphs, compactGraphs.Value.Density);

        // 260px gap -> ReadoutsOnly
        var readouts = TaskbarLayout.SelectResponsiveSlot(
            shell, tray, [Rect(0, 2088, 2926, 2160), weather], candidates, margin: 9);
        Assert.NotNull(readouts);
        Assert.Equal(TaskbarStripDensity.ReadoutsOnly, readouts.Value.Density);

        // 150px gap -> Mini
        var mini = TaskbarLayout.SelectResponsiveSlot(
            shell, tray, [Rect(0, 2088, 3036, 2160), weather], candidates, margin: 9);
        Assert.NotNull(mini);
        Assert.Equal(TaskbarStripDensity.Mini, mini.Value.Density);

        // 80px gap -> IconOnly
        var icon = TaskbarLayout.SelectResponsiveSlot(
            shell, tray, [Rect(0, 2088, 3106, 2160), weather], candidates, margin: 9);
        Assert.NotNull(icon);
        Assert.Equal(TaskbarStripDensity.IconOnly, icon.Value.Density);

        // 30px gap (smaller than IconOnly + margins) -> null
        var hidden = TaskbarLayout.SelectResponsiveSlot(
            shell, tray, [Rect(0, 2088, 3156, 2160), weather], candidates, margin: 9);
        Assert.Null(hidden);
    }

    [Fact]
    public void SelectResponsiveSlotAppliesUpgradeHysteresisWithoutBlockingDowngrades()
    {
        var shell = Rect(0, 2088, 3840, 2160);
        var tray = Rect(3432, 2088, 3840, 2160);
        var weather = Rect(3186, 2088, 3414, 2160);
        var candidates = new[]
        {
            new TaskbarDensityCandidate(TaskbarStripDensity.Full, 552),
            new TaskbarDensityCandidate(TaskbarStripDensity.CompactGraphs, 360)
        };

        // Available gap before weather is 3186 - 2610 - 18 = 558px (fits 552, does NOT fit 552 + 12 = 564)
        var borderlineObstacles = new[] { Rect(0, 2088, 2610, 2160), weather };

        // While already in Full, stays in Full (no hysteresis required to hold current mode)
        var stayFull = TaskbarLayout.SelectResponsiveSlot(
            shell, tray, borderlineObstacles, candidates,
            margin: 9,
            currentVisibleDensity: TaskbarStripDensity.Full,
            upgradeHysteresis: 12);
        Assert.NotNull(stayFull);
        Assert.Equal(TaskbarStripDensity.Full, stayFull.Value.Density);

        // While in CompactGraphs, waits for extra breathing room before upgrading back to Full
        var holdCompact = TaskbarLayout.SelectResponsiveSlot(
            shell, tray, borderlineObstacles, candidates,
            margin: 9,
            currentVisibleDensity: TaskbarStripDensity.CompactGraphs,
            upgradeHysteresis: 12);
        Assert.NotNull(holdCompact);
        Assert.Equal(TaskbarStripDensity.CompactGraphs, holdCompact.Value.Density);

        // Once gap widens to >= 564px, upgrades cleanly back to Full
        var upgradeToFull = TaskbarLayout.SelectResponsiveSlot(
            shell, tray, [Rect(0, 2088, 2590, 2160), weather], candidates,
            margin: 9,
            currentVisibleDensity: TaskbarStripDensity.CompactGraphs,
            upgradeHysteresis: 12);
        Assert.NotNull(upgradeToFull);
        Assert.Equal(TaskbarStripDensity.Full, upgradeToFull.Value.Density);
    }

    [Fact]
    public void ExpectedStripWidthsDecreaseMonotonicallyAcrossDensities()
    {
        foreach (var ramMode in Enum.GetValues<RamDisplayMode>())
        {
            foreach (var barMode in Enum.GetValues<KeepAwakeBarMode>())
            {
                foreach (var hasSensors in new[] { true, false })
                {
                    var settings = new NitroSettings
                    {
                        RamDisplayMode = ramMode,
                        KeepAwakeBarMode = barMode
                    };

                    var full = TaskbarStripWindow.CalculateExpectedWidthDip(TaskbarStripDensity.Full, settings, hasSensors);
                    var compact = TaskbarStripWindow.CalculateExpectedWidthDip(TaskbarStripDensity.CompactGraphs, settings, hasSensors);
                    var readouts = TaskbarStripWindow.CalculateExpectedWidthDip(TaskbarStripDensity.ReadoutsOnly, settings, hasSensors);
                    var mini = TaskbarStripWindow.CalculateExpectedWidthDip(TaskbarStripDensity.Mini, settings, hasSensors);
                    var icon = TaskbarStripWindow.CalculateExpectedWidthDip(TaskbarStripDensity.IconOnly, settings, hasSensors);

                    Assert.True(full >= compact);
                    Assert.True(compact > readouts);
                    Assert.True(readouts > mini);
                    Assert.True(mini > icon);
                    Assert.Equal(36.0, icon);
                }
            }
        }
    }

    [Fact]
    public void TrayTooltipFormatsLiveTelemetryWithinWin32CharacterLimit()
    {
        var snapshot = new MetricsSnapshot(
            CpuTotalPercent: 87.6,
            CpuCorePercents: [87.6],
            CpuHistoryPercents: [87.6],
            RamUsedGb: 24.4,
            RamTotalGb: 32.0,
            RamPercent: 76.2,
            RamHistoryPercents: [76.2],
            LogicalCoreCount: 24,
            CpuFrequencyGhz: 4.25,
            CpuTemperatureCelsius: 74.3,
            TopProcesses: Array.Empty<ProcessUsageItem>());

        var awakeTooltip = TrayIconService.FormatTooltipText(
            snapshot,
            awake: true,
            remaining: TimeSpan.FromHours(4),
            boostDisabled: false);
        Assert.Contains("CPU 88% 74°C", awakeTooltip);
        Assert.Contains("RAM 76%", awakeTooltip);
        Assert.Contains("Awake", awakeTooltip);
        Assert.True(awakeTooltip.Length <= 63);

        var quietTooltip = TrayIconService.FormatTooltipText(
            snapshot,
            awake: false,
            remaining: null,
            boostDisabled: true);
        Assert.Contains("Quiet", quietTooltip);
        Assert.True(quietTooltip.Length <= 63);
    }

    [Fact]
    public void MeasuredWpfStripWidthsMatchExpectedWidthsAcrossAllModes()
    {
        Exception? threadFailure = null;
        var staThread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    var app = new System.Windows.Application
                    {
                        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                    };
                    app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/NitroPC;component/Themes/NitroTheme.xaml", UriKind.Absolute)
                    });
                }

                var window = new TaskbarStripWindow(new NitroSettings());
                foreach (var ramMode in Enum.GetValues<RamDisplayMode>())
                {
                    foreach (var barMode in Enum.GetValues<KeepAwakeBarMode>())
                    {
                        var settings = new NitroSettings
                        {
                            RamDisplayMode = ramMode,
                            KeepAwakeBarMode = barMode
                        };
                        window.ApplySettings(settings);

                        foreach (var hasSensors in new[] { true, false })
                        {
                            foreach (var density in Enum.GetValues<TaskbarStripDensity>())
                            {
                                var expected = TaskbarStripWindow.CalculateExpectedWidthDip(density, settings, hasSensors);
                                var measured = window.MeasureDensityWidthDipForTesting(density, hasSensors);
                                Assert.Equal(expected, measured, precision: 2);
                            }
                        }
                    }
                }

                window.Close();
            }
            catch (Exception ex)
            {
                threadFailure = ex;
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        if (threadFailure is not null)
        {
            throw new InvalidOperationException("STA layout verification failed.", threadFailure);
        }
    }

    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom) =>
        new()
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
}
