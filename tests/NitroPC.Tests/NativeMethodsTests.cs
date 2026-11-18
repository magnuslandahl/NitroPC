using System.Runtime.InteropServices;
using NitroPC.Interop;

namespace NitroPC.Tests;

public sealed class NativeMethodsTests
{
    [Fact]
    public void ProcessorPerformanceInformationHasVerifiedLayout()
    {
        Assert.Equal(48, Marshal.SizeOf<NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>());
    }

    [Fact]
    public void KeyboardInputIncludesLargestUnionMember()
    {
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    [Fact]
    public void MemoryStatusHasExpectedLayout()
    {
        Assert.Equal(64, Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>());
    }
}
