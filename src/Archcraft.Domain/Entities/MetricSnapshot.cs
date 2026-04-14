namespace Archcraft.Domain.Entities;

public sealed record MetricSnapshot
{
    public required string ScenarioName { get; init; }
    public required double P50Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double ErrorRate { get; init; }
    public required int TotalRequests { get; init; }

    /// <summary>How many requests the scenario intended to send (RPS × duration). Zero when unknown.</summary>
    public int TargetRequests { get; init; }

    /// <summary>Fraction of target requests actually issued. 1.0 when TargetRequests is unknown.</summary>
    public double Saturation => TargetRequests > 0 ? (double)TotalRequests / TargetRequests : 1.0;

    /// <summary>Raw latency measurements in milliseconds for histogram.</summary>
    public required IReadOnlyList<double> RawLatenciesMs { get; init; }

    /// <summary>Per-replica breakdown. Null when service has a single instance.</summary>
    public IReadOnlyDictionary<string, MetricSnapshot>? ReplicaSnapshots { get; init; }
}
