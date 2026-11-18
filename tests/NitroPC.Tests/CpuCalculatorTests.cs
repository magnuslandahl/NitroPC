using NitroPC.Helpers;
using NitroPC.Interop;

namespace NitroPC.Tests;

public sealed class CpuCalculatorTests
{
    [Fact]
    public void IdleProcessorReportsZero()
    {
        var result = CpuCalculator.ComputeUsage(
            [Counter(100, 200, 0)],
            [Counter(200, 300, 0)]);

        Assert.Equal(0, result.TotalPercent);
        Assert.Equal(0, result.CorePercents[0]);
    }

    [Fact]
    public void ProcessorAtHalfBusyReportsFiftyPercent()
    {
        var result = CpuCalculator.ComputeUsage(
            [Counter(100, 200, 10)],
            [Counter(150, 300, 10)]);

        Assert.Equal(50, result.TotalPercent, precision: 8);
        Assert.Equal(50, result.CorePercents[0], precision: 8);
    }

    [Fact]
    public void DownsampleMapsTwentyFourCoresToFourteenBuckets()
    {
        var values = Enumerable.Range(1, 24).Select(index => (double)index).ToArray();

        var bars = CpuCalculator.DownsampleBars(values, 14);

        Assert.Equal(14, bars.Length);
        Assert.Equal(1, bars[0], precision: 8);
        Assert.Equal(4.5, bars[2], precision: 8);
        Assert.Equal(23.5, bars[^1], precision: 8);
    }

    private static NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION Counter(
        long idle,
        long kernel,
        long user) =>
        new()
        {
            IdleTime = idle,
            KernelTime = kernel,
            UserTime = user
        };
}
