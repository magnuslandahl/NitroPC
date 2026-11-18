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
    }

    [Fact]
    public void DynamicRamWindowZoomsSmallChangesIntoVisibleRange()
    {
        var (min, max) = RamSparklineControl.ComputeDynamicWindow([61.1, 61.4, 61.8], 0, 3);

        Assert.InRange(max - min, 2.4, 3.0);
        Assert.True(min < 61.1);
        Assert.True(max > 61.8);
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
