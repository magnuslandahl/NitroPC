using System.Globalization;

namespace NitroPC.Interop;

internal sealed class CpuSensorReader : IDisposable
{
    private readonly IntPtr _query;
    private readonly IntPtr _totalBaseFrequencyCounter;
    private readonly IntPtr _totalPerformanceCounter;
    private readonly List<(IntPtr BaseFrequencyCounter, IntPtr PerformanceCounter)> _coreCounters = [];
    private readonly List<(IntPtr Counter, bool IsDeciKelvin)> _temperatureCounters = [];
    private bool _disposed;

    public CpuSensorReader()
    {
        if (NativeMethods.PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _query) != 0 ||
            _query == IntPtr.Zero)
        {
            _query = IntPtr.Zero;
            return;
        }

        NativeMethods.PdhAddEnglishCounter(
            _query,
            @"\Processor Information(_Total)\Processor Frequency",
            IntPtr.Zero,
            out _totalBaseFrequencyCounter);
        NativeMethods.PdhAddEnglishCounter(
            _query,
            @"\Processor Information(_Total)\% Processor Performance",
            IntPtr.Zero,
            out _totalPerformanceCounter);

        AddPerCoreFrequencyCounters();
        AddThermalZoneCounters();
        NativeMethods.PdhCollectQueryData(_query);
    }

    public (double? FrequencyGhz, double? TemperatureCelsius) Read(IReadOnlyList<double> coreBusyPercents)
    {
        if (_disposed || _query == IntPtr.Zero)
        {
            return (null, null);
        }

        if (NativeMethods.PdhCollectQueryData(_query) != 0)
        {
            return (null, null);
        }

        var frequencyGhz = ReadEffectiveFrequencyGhz(coreBusyPercents);
        var temperatureCelsius = ReadTemperatureCelsius();
        return (frequencyGhz, temperatureCelsius);
    }

    internal static double? ComputeWeightedFrequencyGhz(
        IReadOnlyList<(double BaseMhz, double PerformancePercent)> cores,
        IReadOnlyList<double> coreBusyPercents,
        (double BaseMhz, double PerformancePercent)? fallbackTotal)
    {
        if (cores.Count > 0)
        {
            var count = Math.Min(cores.Count, coreBusyPercents.Count);
            var weightedGhzSum = 0.0;
            var weightSum = 0.0;
            var simpleGhzSum = 0.0;
            var validCoreCount = 0;

            for (var index = 0; index < cores.Count; index++)
            {
                var (baseMhz, perfPct) = cores[index];
                if (baseMhz <= 0 || perfPct <= 0)
                {
                    continue;
                }

                var coreGhz = baseMhz * (perfPct / 100.0) / 1000.0;
                if (coreGhz is < 0.1 or > 10.0)
                {
                    continue;
                }

                simpleGhzSum += coreGhz;
                validCoreCount++;

                if (index < count)
                {
                    var weight = Math.Max(0.0, coreBusyPercents[index]);
                    weightedGhzSum += coreGhz * weight;
                    weightSum += weight;
                }
            }

            if (weightSum >= 2.0)
            {
                return weightedGhzSum / weightSum;
            }

            if (validCoreCount > 0)
            {
                return simpleGhzSum / validCoreCount;
            }
        }

        if (fallbackTotal is { BaseMhz: > 0, PerformancePercent: > 0 } total)
        {
            var ghz = total.BaseMhz * (total.PerformancePercent / 100.0) / 1000.0;
            return ghz is >= 0.1 and <= 10.0 ? ghz : null;
        }

        return null;
    }

    private double? ReadEffectiveFrequencyGhz(IReadOnlyList<double> coreBusyPercents)
    {
        var coreReadings = new List<(double BaseMhz, double PerformancePercent)>(_coreCounters.Count);
        foreach (var (freqCounter, perfCounter) in _coreCounters)
        {
            if (TryReadDouble(freqCounter, out var baseMhz) &&
                TryReadDouble(perfCounter, out var perfPct))
            {
                coreReadings.Add((baseMhz, perfPct));
            }
        }

        (double BaseMhz, double PerformancePercent)? fallbackTotal = null;
        if (TryReadDouble(_totalBaseFrequencyCounter, out var totalBase) &&
            TryReadDouble(_totalPerformanceCounter, out var totalPerf))
        {
            fallbackTotal = (totalBase, totalPerf);
        }

        return ComputeWeightedFrequencyGhz(coreReadings, coreBusyPercents, fallbackTotal);
    }

    private double? ReadTemperatureCelsius()
    {
        double? maxCelsius = null;
        foreach (var (counter, isDeciKelvin) in _temperatureCounters)
        {
            if (!TryReadDouble(counter, out var raw) || raw <= 0)
            {
                continue;
            }

            var kelvin = isDeciKelvin ? raw / 10.0 : raw;
            var celsius = kelvin - 273.15;
            if (celsius is < 1.0 or > 125.0)
            {
                continue;
            }

            if (maxCelsius is null || celsius > maxCelsius.Value)
            {
                maxCelsius = celsius;
            }
        }

        return maxCelsius;
    }

    private void AddPerCoreFrequencyCounters()
    {
        var perfPaths = ExpandWildcard(@"\Processor Information(*)\% Processor Performance");
        var coreInstances = new List<(int Group, int Core, string Instance)>();

        foreach (var path in perfPaths)
        {
            var instance = ExtractInstanceName(path);
            if (string.IsNullOrEmpty(instance) ||
                instance.EndsWith("_Total", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = instance.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var core))
            {
                coreInstances.Add((group, core, instance));
            }
        }

        foreach (var (_, _, instance) in coreInstances.OrderBy(item => item.Group).ThenBy(item => item.Core))
        {
            var freqPath = $@"\Processor Information({instance})\Processor Frequency";
            var perfPath = $@"\Processor Information({instance})\% Processor Performance";
            if (NativeMethods.PdhAddEnglishCounter(_query, freqPath, IntPtr.Zero, out var freqCounter) == 0 &&
                NativeMethods.PdhAddEnglishCounter(_query, perfPath, IntPtr.Zero, out var perfCounter) == 0)
            {
                _coreCounters.Add((freqCounter, perfCounter));
            }
        }
    }

    private void AddThermalZoneCounters()
    {
        var highPrecisionPaths = ExpandWildcard(@"\Thermal Zone Information(*)\High Precision Temperature");
        foreach (var path in highPrecisionPaths)
        {
            if (NativeMethods.PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter) == 0)
            {
                _temperatureCounters.Add((counter, IsDeciKelvin: true));
            }
        }

        if (_temperatureCounters.Count > 0)
        {
            return;
        }

        var standardPaths = ExpandWildcard(@"\Thermal Zone Information(*)\Temperature");
        foreach (var path in standardPaths)
        {
            if (NativeMethods.PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter) == 0)
            {
                _temperatureCounters.Add((counter, IsDeciKelvin: false));
            }
        }
    }

    private static IReadOnlyList<string> ExpandWildcard(string wildcardPath)
    {
        uint length = 0;
        _ = NativeMethods.PdhExpandWildCardPath(IntPtr.Zero, wildcardPath, null, ref length, 0);
        if (length == 0 || length > 65536)
        {
            return Array.Empty<string>();
        }

        var buffer = new char[length];
        if (NativeMethods.PdhExpandWildCardPath(IntPtr.Zero, wildcardPath, buffer, ref length, 0) != 0)
        {
            return Array.Empty<string>();
        }

        return new string(buffer, 0, (int)length)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? ExtractInstanceName(string counterPath)
    {
        var open = counterPath.IndexOf('(');
        var close = counterPath.IndexOf(')', open + 1);
        return open >= 0 && close > open + 1
            ? counterPath[(open + 1)..close]
            : null;
    }

    private static bool TryReadDouble(IntPtr counter, out double value)
    {
        value = 0;
        if (counter == IntPtr.Zero)
        {
            return false;
        }

        var status = NativeMethods.PdhGetFormattedCounterValue(
            counter,
            NativeMethods.PdhFormatDouble,
            IntPtr.Zero,
            out var formatted);
        if (status != 0 || formatted.CStatus != 0 || double.IsNaN(formatted.doubleValue))
        {
            return false;
        }

        value = formatted.doubleValue;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_query != IntPtr.Zero)
        {
            NativeMethods.PdhCloseQuery(_query);
        }

        _disposed = true;
    }
}
