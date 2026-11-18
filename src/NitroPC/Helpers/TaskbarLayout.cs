using NitroPC.Interop;

namespace NitroPC.Helpers;

internal static class TaskbarLayout
{
    public static int? FindAvailableSlot(
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

        var minimumX = shell.Left + margin;
        var maximumX = tray.Left - margin - width;
        if (maximumX < minimumX)
        {
            return null;
        }

        // Place the strip immediately after the task-list rebar. Windows 11
        // renders Widgets/Weather inside a full-width composition window, so
        // anchoring beside the tray cannot detect or avoid that overlap.
        var candidate = rebar is { } value
            ? Math.Clamp(value.Right + margin, minimumX, maximumX)
            : maximumX;

        while (candidate <= maximumX)
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

            candidate = colliding.Value.Right + margin;
        }

        return null;
    }

    private static bool Overlaps(int left, int width, int otherLeft, int otherRight) =>
        left < otherRight && left + width > otherLeft;
}
