using NitroPC.Helpers;
using NitroPC.Interop;

namespace NitroPC.Tests;

public sealed class TaskbarLayoutTests
{
    [Fact]
    public void PlacesStripImmediatelyBeforeTrayWhenClear()
    {
        var x = TaskbarLayout.FindRightAnchoredSlot(
            Rect(0, 100, 1000, 148),
            Rect(900, 100, 1000, 148),
            Rect(0, 100, 500, 148),
            [],
            width: 260,
            margin: 6);

        Assert.Equal(634, x);
    }

    [Fact]
    public void MovesLeftPastOverlappingTaskbarItem()
    {
        var x = TaskbarLayout.FindRightAnchoredSlot(
            Rect(0, 100, 1000, 148),
            Rect(900, 100, 1000, 148),
            Rect(0, 100, 300, 148),
            [Rect(700, 100, 820, 148)],
            width: 260,
            margin: 6);

        Assert.Equal(434, x);
    }

    [Fact]
    public void ReturnsNullWhenNoCollisionFreeSlotRemains()
    {
        var x = TaskbarLayout.FindRightAnchoredSlot(
            Rect(0, 100, 1000, 148),
            Rect(900, 100, 1000, 148),
            Rect(0, 100, 700, 148),
            [Rect(600, 100, 850, 148)],
            width: 260,
            margin: 6);

        Assert.Null(x);
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
