using NitroPC.Interop;

namespace NitroPC.Helpers;

internal static class CpuCalculator
{
    public static (double TotalPercent, double[] CorePercents) ComputeUsage(
        NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] previous,
        NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var count = Math.Min(previous.Length, current.Length);
        var corePercents = new double[count];
        long totalBusy = 0;
        long totalTime = 0;

        for (var index = 0; index < count; index++)
        {
            var dIdle = Math.Max(0, current[index].IdleTime - previous[index].IdleTime);
            var dKernel = Math.Max(0, current[index].KernelTime - previous[index].KernelTime);
            var dUser = Math.Max(0, current[index].UserTime - previous[index].UserTime);
            var total = dKernel + dUser;
            var busy = Math.Max(0, (dKernel - dIdle) + dUser);

            totalTime += total;
            totalBusy += busy;
            corePercents[index] = total > 0
                ? Math.Clamp((double)busy / total * 100.0, 0.0, 100.0)
                : 0.0;
        }

        var totalPercent = totalTime > 0
            ? Math.Clamp((double)totalBusy / totalTime * 100.0, 0.0, 100.0)
            : 0.0;
        return (totalPercent, corePercents);
    }

    public static double[] DownsampleBars(IReadOnlyList<double> corePercents, int targetBars)
    {
        ArgumentNullException.ThrowIfNull(corePercents);
        if (targetBars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetBars));
        }

        if (corePercents.Count == 0)
        {
            return new double[targetBars];
        }

        if (corePercents.Count <= targetBars)
        {
            return corePercents.ToArray();
        }

        var result = new double[targetBars];
        for (var bucket = 0; bucket < targetBars; bucket++)
        {
            var start = bucket * corePercents.Count / targetBars;
            var end = (bucket + 1) * corePercents.Count / targetBars;
            double sum = 0;
            for (var index = start; index < end; index++)
            {
                sum += corePercents[index];
            }

            result[bucket] = sum / (end - start);
        }

        return result;
    }
}
