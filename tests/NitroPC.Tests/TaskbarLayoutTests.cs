using NitroPC.Helpers;
using NitroPC.Interop;

namespace NitroPC.Tests;

public sealed class TaskbarLayoutTests
{
    [Fact]
    public void PlacesStripImmediatelyAfterTaskListWhenClear()
    {
        var x = TaskbarLayout.FindAvailableSlot(
            Rect(0, 100, 1000, 148),
            Rect(900, 100, 1000, 148),
            Rect(0, 100, 500, 148),
            [],
            width: 260,
            margin: 6);

        Assert.Equal(506, x);
    }

    [Fact]
    public void MovesRightPastOverlappingTaskbarItem()
    {
        var x = TaskbarLayout.FindAvailableSlot(
            Rect(0, 100, 1000, 148),
            Rect(900, 100, 1000, 148),
            Rect(0, 100, 500, 148),
            [Rect(500, 100, 620, 148)],
            width: 260,
            margin: 6);

        Assert.Equal(626, x);
    }

    [Fact]
    public void WeatherAreaDoesNotAffectPostTaskListSlot()
    {
        var x = TaskbarLayout.FindAvailableSlot(
            Rect(0, 100, 1000, 148),
            Rect(900, 100, 1000, 148),
            Rect(0, 100, 300, 148),
            [Rect(780, 100, 900, 148)],
            width: 260,
            margin: 6);

        Assert.Equal(306, x);
    }

    [Fact]
    public void FallsBackToRightAnchorWithoutTaskList()
    {
        var x = TaskbarLayout.FindAvailableSlot(
            Rect(0, 100, 1000, 148),
            Rect(900, 100, 1000, 148),
            null,
            [],
            width: 260,
            margin: 6);

        Assert.Equal(634, x);
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
