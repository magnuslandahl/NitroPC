using NitroPC.Helpers;

namespace NitroPC.Tests;

public sealed class RingBufferTests
{
    [Fact]
    public void FirstPushEstablishesFlatBaseline()
    {
        var buffer = new RingBuffer(4);

        buffer.Push(64);

        Assert.Equal([64, 64, 64, 64], buffer.ToArray());
    }

    [Fact]
    public void PushesShiftOldestValuesOut()
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
