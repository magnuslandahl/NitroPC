using NitroPC.Controls;
using NitroPC.Helpers;
using NitroPC.Interop;
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

    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom) =>
        new()
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
}
