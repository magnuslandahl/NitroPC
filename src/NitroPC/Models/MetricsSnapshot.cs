namespace NitroPC.Models;

public sealed record MetricsSnapshot(
    double CpuTotalPercent,
    double[] CpuCorePercents,
    int LogicalCoreCount,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double[] RamHistoryPercents);
