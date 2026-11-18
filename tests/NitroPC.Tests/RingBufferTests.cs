using NitroPC.Helpers;

namespace NitroPC.Tests;

public sealed class RingBufferTests
{
    [Fact]
    public void FirstPushGrowsProgressivelyInsteadOfPreFilling()
    {
        var buffer = new RingBuffer(4);

        buffer.Push(64);
        Assert.Equal([64], buffer.ToArray());

        buffer.Push(66);
        Assert.Equal([64, 66], buffer.ToArray());
    }

    [Fact]
    public void PushesShiftOldestValuesOutOnceFull()
    {
        var buffer = new RingBuffer(4);
        buffer.Push(10);
        buffer.Push(11);
        buffer.Push(12);
        buffer.Push(13);
        buffer.Push(14);

        Assert.Equal([11, 12, 13, 14], buffer.ToArray());
    }
}
