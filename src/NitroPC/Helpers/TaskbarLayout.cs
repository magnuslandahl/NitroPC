using NitroPC.Interop;

namespace NitroPC.Helpers;

internal enum TaskbarStripDensity
{
    Full = 0,
    CompactGraphs = 1,
    ReadoutsOnly = 2,
    Mini = 3,
    IconOnly = 4
}

internal readonly record struct TaskbarDensityCandidate(
    TaskbarStripDensity Density,
    int Width);

internal readonly record struct TaskbarSlotPlacement(
    TaskbarStripDensity Density,
    int Left,
    int Width);

internal static class TaskbarLayout
{
    public static TaskbarSlotPlacement? SelectResponsiveSlot(
        NativeMethods.RECT shell,
        NativeMethods.RECT tray,
        IReadOnlyList<NativeMethods.RECT> obstacles,
        IReadOnlyList<TaskbarDensityCandidate> orderedCandidates,
        int margin,
        TaskbarStripDensity? currentVisibleDensity = null,
        int upgradeHysteresis = 0)
    {
        if (shell.Width <= 0 || orderedCandidates.Count == 0)
        {
            return null;
        }

        var safeHysteresis = Math.Max(0, upgradeHysteresis);
        foreach (var candidate in orderedCandidates)
        {
            if (candidate.Width <= 0)
            {
                continue;
            }

            var isUpgrade = currentVisibleDensity is { } current && candidate.Density < current;
            if (isUpgrade && safeHysteresis > 0)
            {
                var widerSlot = FindRightAnchoredSlot(
                    shell,
                    tray,
                    obstacles,
                    candidate.Width + safeHysteresis,
                    margin);
                if (widerSlot is null)
                {
                    continue;
                }
            }

            var slot = FindRightAnchoredSlot(
                shell,
                tray,
                obstacles,
                candidate.Width,
                margin);
            if (slot is { } left)
            {
                return new TaskbarSlotPlacement(candidate.Density, left, candidate.Width);
            }
        }

        return null;
    }

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
