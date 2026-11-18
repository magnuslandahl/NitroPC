using NitroPC.Interop;

namespace NitroPC.Helpers;

internal static class TaskbarLayout
{
    public static int? FindRightAnchoredSlot(
        NativeMethods.RECT shell,
        NativeMethods.RECT tray,
        NativeMethods.RECT? rebar,
        IReadOnlyList<NativeMethods.RECT> obstacles,
        int width,
        int margin)
    {
        if (width <= 0 || shell.Width <= 0)
        {
            return null;
        }

        var minimumX = rebar is { } value
            ? Math.Max(shell.Left + margin, value.Right + margin)
            : shell.Left + margin;
        var candidate = tray.Left - margin - width;

        while (candidate >= minimumX)
        {
            var colliding = obstacles
                .Where(rect => Overlaps(candidate, width, rect.Left, rect.Right))
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

    private static bool Overlaps(int left, int width, int otherLeft, int otherRight) =>
        left < otherRight && left + width > otherLeft;
}
