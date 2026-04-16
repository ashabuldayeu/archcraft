using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public sealed record ReadinessConfig
{
    /// <summary>HTTP path for HTTP-based readiness check.</summary>
    public string? Path { get; init; }
    /// <summary>Log message pattern for log-based readiness check (e.g. cluster nodes).</summary>
    public string? LogPattern { get; init; }
    /// <summary>TCP port to poll until it accepts connections (e.g. Kafka).</summary>
    public int? TcpPort { get; init; }
    public required Duration Timeout { get; init; }
}
