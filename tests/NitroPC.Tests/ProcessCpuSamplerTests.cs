using NitroPC.Helpers;
using NitroPC.Interop;

namespace NitroPC.Tests;

public sealed class ProcessCpuSamplerTests
{
    [Fact]
    public void AggregatesMultiInstanceAppsIgnoresReusedPidsAndReturnsTopFive()
    {
        var previous = new Dictionary<(long Pid, long CreateTime), long>
        {
            [(100, 1)] = 1000,
            [(101, 1)] = 1000,
            [(200, 1)] = 500,
            [(300, 1)] = 200,
            [(400, 1)] = 100,
            [(500, 1)] = 100,
            [(600, 1)] = 100,
            [(700, 1)] = 900,
            [(801, 1)] = 100,
            [(802, 1)] = 100
        };

        var current = new List<ProcessCpuSampler.RawProcessSample>
        {
            new(100, 1, "msedge.exe", 1800),
            new(101, 1, "msedge.exe", 1700),
            new(200, 1, "SentinelAgent.exe", 1700),
            new(300, 1, "Code.exe", 800),
            new(400, 1, "Teams.exe", 500),
            new(500, 1, "dwm.exe", 350),
            new(600, 1, "notepad.exe", 200),
            new(700, 2, "spawned.exe", 9500),
            // Separate svchost hosts stay per-PID rather than summing into a synthetic top entry
            new(801, 1, "svchost.exe", 300),
            new(802, 1, "svchost.exe", 300)
        };

        var top = ProcessCpuSampler.ComputeTopProcesses(previous, current, totalSystemDeltaTicks: 10000, maxItems: 5);

        Assert.Equal(5, top.Count);
        Assert.Equal("msedge", top[0].Name);
        Assert.Equal(2, top[0].InstanceCount);
        Assert.Equal(15.0, top[0].CpuPercent, precision: 6);
        Assert.Equal("SentinelAgent", top[1].Name);
        Assert.Equal(12.0, top[1].CpuPercent, precision: 6);
        Assert.Equal("Code", top[2].Name);
        Assert.Equal("Teams", top[3].Name);
        Assert.Equal("dwm", top[4].Name);
        Assert.DoesNotContain(top, item => item.Name == "spawned");
    }

    [Fact]
    public void WeightedFrequencyReflectsActiveBoostingCore()
    {
        var cores = new (double BaseMhz, double PerformancePercent)[]
        {
            (2000, 230), // 4.60 GHz boosting core
            (2000, 90),  // 1.80 GHz idle core
            (2000, 90),
            (2000, 90)
        };
        var busy = new double[] { 90.0, 0.0, 0.0, 0.0 };

        var ghz = CpuSensorReader.ComputeWeightedFrequencyGhz(cores, busy, (2000, 100));

        Assert.NotNull(ghz);
        Assert.Equal(4.60, ghz.Value, precision: 2);
    }
}
