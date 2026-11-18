using NitroPC.Interop;

namespace NitroPC.Helpers;

internal static class TaskbarLayout
{
    public static int? FindRightAnchoredSlot(
        NativeMethods.RECT shell,
        NativeMethods.RECT tray,
        IReadOnlyList<NativeMethods.RECT> obstacles,
        int width,
        int margin)
    {
        if (width <= 0 || shell.Width <= 0)
        {
            return null;
        }

        var minimumX = shell.Left + margin;
        var candidate = tray.Left - margin - width;

        while (candidate >= minimumX)
        {
            var colliding = obstacles
                .Where(rect => OverlapsWithMargin(candidate, width, rect.Left, rect.Right, margin))
                .OrderBy(rect => rect.Left)
                .Cast<NativeMethods.RECT?>()
                .FirstOrDefault();

            if (colliding is null)
            {
                return candidate;
            }

            candidate = colliding.Value.Left - margin - width;
        }

        return null;
    }

    private static bool OverlapsWithMargin(int left, int width, int otherLeft, int otherRight, int margin) =>
        left < otherRight + margin && left + width > otherLeft - margin;
}
