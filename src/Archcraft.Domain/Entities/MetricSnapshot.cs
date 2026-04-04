namespace Archcraft.Domain.Entities;

public sealed record MetricSnapshot
{
    public required string ScenarioName { get; init; }
    public required double P50Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double ErrorRate { get; init; }
    public required int TotalRequests { get; init; }

    /// <summary>Raw latency measurements in milliseconds for histogram.</summary>
    public required IReadOnlyList<double> RawLatenciesMs { get; init; }

    /// <summary>Per-replica breakdown. Null when service has a single instance.</summary>
    public IReadOnlyDictionary<string, MetricSnapshot>? ReplicaSnapshots { get; init; }
}
