using NitroPC.Services;

namespace NitroPC.Tests;

public sealed class KeepAwakeServiceTests
{
    [Fact]
    public void TimedSessionStartsAndAutomaticallyStops()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var platform = new FakePlatform();
        using var service = new KeepAwakeService(platform, () => now);

        service.Start(TimeSpan.FromMinutes(5));

        Assert.True(service.IsAwake);
        Assert.Equal(TimeSpan.FromMinutes(5), service.Remaining);
        Assert.Equal(1, platform.EnableCount);
        Assert.Equal(1, platform.PulseCount);

        now = now.AddMinutes(5);
        service.EvaluateExpiration();

        Assert.False(service.IsAwake);
        Assert.Equal(1, platform.ClearCount);
    }

    [Fact]
    public void IndefiniteSessionOnlyStopsExplicitly()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var platform = new FakePlatform();
        using var service = new KeepAwakeService(platform, () => now);

        service.Start(null);
        now = now.AddDays(30);
        service.EvaluateExpiration();

        Assert.True(service.IsAwake);
        Assert.True(service.IsIndefinite);
        Assert.Null(service.Remaining);
        service.Stop();
        Assert.False(service.IsAwake);
        Assert.Equal(1, platform.ClearCount);
    }

    private sealed class FakePlatform : IKeepAwakePlatform
    {
        public int EnableCount { get; private set; }
        public int PulseCount { get; private set; }
        public int ClearCount { get; private set; }

        public void EnableExecutionState() => EnableCount++;

        public void SendActivityPulse() => PulseCount++;

        public void ClearExecutionState() => ClearCount++;
    }
}
