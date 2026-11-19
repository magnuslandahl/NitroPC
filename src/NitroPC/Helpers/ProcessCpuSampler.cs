using System.Runtime.InteropServices;
using NitroPC.Interop;
using NitroPC.Models;

namespace NitroPC.Helpers;

internal sealed class ProcessCpuSampler : IDisposable
{
    internal readonly record struct RawProcessSample(
        long Pid,
        long CreateTime,
        string Name,
        long CpuTicks);

    private readonly Dictionary<(long Pid, long CreateTime), long> _previousTicks = [];
    private IntPtr _buffer;
    private int _bufferSize;
    private bool _hasBaseline;
    private bool _disposed;

    public ProcessCpuSampler()
    {
        if (IntPtr.Size == 8)
        {
            _bufferSize = 512 * 1024;
            _buffer = Marshal.AllocHGlobal(_bufferSize);
        }
    }

    public void PrimeBaseline()
    {
        if (_disposed || _buffer == IntPtr.Zero)
        {
            return;
        }

        var current = ReadCurrentProcesses();
        _previousTicks.Clear();
        foreach (var sample in current)
        {
            _previousTicks[(sample.Pid, sample.CreateTime)] = sample.CpuTicks;
        }

        _hasBaseline = true;
    }

    public IReadOnlyList<ProcessUsageItem> SampleTopProcesses(long totalSystemDeltaTicks, int maxItems = 5)
    {
        if (_disposed || _buffer == IntPtr.Zero)
        {
            return Array.Empty<ProcessUsageItem>();
        }

        var current = ReadCurrentProcesses();
        if (!_hasBaseline || totalSystemDeltaTicks <= 0)
        {
            _previousTicks.Clear();
            foreach (var sample in current)
            {
                _previousTicks[(sample.Pid, sample.CreateTime)] = sample.CpuTicks;
            }

            _hasBaseline = true;
            return Array.Empty<ProcessUsageItem>();
        }

        var result = ComputeTopProcesses(_previousTicks, current, totalSystemDeltaTicks, maxItems);

        _previousTicks.Clear();
        foreach (var sample in current)
        {
            _previousTicks[(sample.Pid, sample.CreateTime)] = sample.CpuTicks;
        }

        return result;
    }

    internal static IReadOnlyList<ProcessUsageItem> ComputeTopProcesses(
        IReadOnlyDictionary<(long Pid, long CreateTime), long> previousTicks,
        IReadOnlyList<RawProcessSample> currentSamples,
        long totalSystemDeltaTicks,
        int maxItems = 5)
    {
        if (totalSystemDeltaTicks <= 0 || maxItems <= 0 || currentSamples.Count == 0)
        {
            return Array.Empty<ProcessUsageItem>();
        }

        var grouped = new Dictionary<string, (string DisplayName, double CpuPercent, int Count, double PeakSingle)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var sample in currentSamples)
        {
            if (sample.Pid == 0)
            {
                continue;
            }

            var key = (sample.Pid, sample.CreateTime);
            if (!previousTicks.TryGetValue(key, out var previous))
            {
                continue;
            }

            var deltaTicks = sample.CpuTicks - previous;
            if (deltaTicks <= 0)
            {
                continue;
            }

            var percent = Math.Clamp((double)deltaTicks / totalSystemDeltaTicks * 100.0, 0.0, 100.0);
            var cleanName = NormalizeDisplayName(sample.Name, sample.Pid);
            var groupKey = cleanName.Equals("svchost", StringComparison.OrdinalIgnoreCase)
                ? $"svchost#{sample.Pid}"
                : cleanName;

            if (grouped.TryGetValue(groupKey, out var existing))
            {
                var displayName = percent > existing.PeakSingle ? cleanName : existing.DisplayName;
                grouped[groupKey] = (
                    displayName,
                    Math.Min(100.0, existing.CpuPercent + percent),
                    existing.Count + 1,
                    Math.Max(existing.PeakSingle, percent));
            }
            else
            {
                grouped[groupKey] = (cleanName, percent, 1, percent);
            }
        }

        return grouped.Values
            .Where(item => item.CpuPercent >= 0.05)
            .OrderByDescending(item => item.CpuPercent)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .Select(item => new ProcessUsageItem(item.DisplayName, item.CpuPercent, item.Count))
            .ToArray();
    }

    internal static string NormalizeDisplayName(string rawName, long pid)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return pid == 4 ? "System" : $"PID {pid}";
        }

        var trimmed = rawName.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 4)
        {
            return trimmed[..^4];
        }

        return trimmed;
    }

    private List<RawProcessSample> ReadCurrentProcesses()
    {
        while (true)
        {
            var status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemProcessInformation,
                _buffer,
                _bufferSize,
                out var needed);
            if (status == NativeMethods.StatusInfoLengthMismatch)
            {
                Marshal.FreeHGlobal(_buffer);
                _bufferSize = Math.Max(_bufferSize * 2, needed + 65536);
                _buffer = Marshal.AllocHGlobal(_bufferSize);
                continue;
            }

            if (status < 0)
            {
                return [];
            }

            break;
        }

        var samples = new List<RawProcessSample>(256);
        var current = _buffer;
        while (true)
        {
            var nextOffset = unchecked((uint)Marshal.ReadInt32(current, 0));
            var createTime = Marshal.ReadInt64(current, 32);
            var userTime = Marshal.ReadInt64(current, 40);
            var kernelTime = Marshal.ReadInt64(current, 48);
            var nameLengthBytes = unchecked((ushort)Marshal.ReadInt16(current, 56));
            var namePointer = Marshal.ReadIntPtr(current, 64);
            var pid = Marshal.ReadIntPtr(current, 80).ToInt64();

            if (pid != 0)
            {
                var name = namePointer != IntPtr.Zero && nameLengthBytes >= 2
                    ? Marshal.PtrToStringUni(namePointer, nameLengthBytes / 2) ?? string.Empty
                    : pid == 4 ? "System" : string.Empty;
                samples.Add(new RawProcessSample(pid, createTime, name, userTime + kernelTime));
            }

            if (nextOffset == 0)
            {
                break;
            }

            current = IntPtr.Add(current, unchecked((int)nextOffset));
        }

        return samples;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }

        _previousTicks.Clear();
        _disposed = true;
    }
}
