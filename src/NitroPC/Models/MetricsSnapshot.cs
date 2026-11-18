namespace NitroPC.Models;

public sealed record ProcessUsageItem(
    string Name,
    double CpuPercent,
    int InstanceCount);

public sealed record MetricsSnapshot(
    double CpuTotalPercent,
    double[] CpuCorePercents,
    double[] CpuHistoryPercents,
    int LogicalCoreCount,
    double? CpuFrequencyGhz,
    double? CpuTemperatureCelsius,
    IReadOnlyList<ProcessUsageItem> TopProcesses,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double[] RamHistoryPercents);
