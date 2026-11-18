using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NitroPC.Helpers;
using NitroPC.Interop;
using NitroPC.Models;

namespace NitroPC.Services;

public sealed class SystemMetricsService : IDisposable
{
    public const int HistoryCapacity = 30;
    public const int SampleIntervalSeconds = 2;

    private readonly DispatcherTimer _timer;
    private readonly RingBuffer _cpuHistory = new(HistoryCapacity);
    private readonly RingBuffer _ramHistory = new(HistoryCapacity);
    private NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[]? _previous;
    private bool _priming;
    private bool _disposed;

    public SystemMetricsService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(SampleIntervalSeconds)
        };
        _timer.Tick += TimerOnTick;
    }

    public event Action<MetricsSnapshot>? MetricsUpdated;

    public event Action<Exception>? SamplingFailed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_timer.IsEnabled)
        {
            return;
        }

        _previous = ReadProcessorInformation();
        _priming = true;
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _priming = false;
    }

    private void TimerOnTick(object? sender, EventArgs e)
    {
        try
        {
            if (_priming)
            {
                _priming = false;
                _timer.Interval = TimeSpan.FromSeconds(SampleIntervalSeconds);
            }

            Sample();
        }
        catch (InvalidOperationException exception)
        {
            Stop();
            SamplingFailed?.Invoke(exception);
        }
        catch (Win32Exception exception)
        {
            Stop();
            SamplingFailed?.Invoke(exception);
        }
    }

    private void Sample()
    {
        var current = ReadProcessorInformation();
        var usage = _previous is null
            ? (TotalPercent: 0.0, CorePercents: new double[current.Length])
            : CpuCalculator.ComputeUsage(_previous, current);
        _previous = current;
        _cpuHistory.Push(usage.TotalPercent);

        var memory = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
        };
        if (!NativeMethods.GlobalMemoryStatusEx(ref memory))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read system memory.");
        }

        var usedBytes = memory.ullTotalPhys >= memory.ullAvailPhys
            ? memory.ullTotalPhys - memory.ullAvailPhys
            : 0;
        var ramPercent = memory.ullTotalPhys == 0
            ? 0.0
            : Math.Clamp((double)usedBytes / memory.ullTotalPhys * 100.0, 0.0, 100.0);
        _ramHistory.Push(ramPercent);

        MetricsUpdated?.Invoke(new MetricsSnapshot(
            usage.TotalPercent,
            usage.CorePercents,
            _cpuHistory.ToArray(),
            usage.CorePercents.Length,
            ramPercent,
            usedBytes / 1024.0 / 1024.0 / 1024.0,
            memory.ullTotalPhys / 1024.0 / 1024.0 / 1024.0,
            _ramHistory.ToArray()));
    }

    private static NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] ReadProcessorInformation()
    {
        var values = new NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[Environment.ProcessorCount];
        var bufferLength = values.Length * Marshal.SizeOf<NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
        var status = NativeMethods.NtQuerySystemInformation(
            NativeMethods.SystemProcessorPerformanceInformation,
            values,
            bufferLength,
            out var returnedLength);

        if (status < 0 || returnedLength > bufferLength)
        {
            throw new InvalidOperationException($"Unable to read processor counters (NTSTATUS 0x{status:X8}).");
        }

        return values;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _timer.Tick -= TimerOnTick;
        _disposed = true;
    }
}
