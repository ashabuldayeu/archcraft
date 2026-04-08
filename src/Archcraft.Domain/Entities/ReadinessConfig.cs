using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public sealed record ReadinessConfig
{
    /// <summary>HTTP path for HTTP-based readiness check. Mutually exclusive with LogPattern.</summary>
    public string? Path { get; init; }
    public required Duration Timeout { get; init; }
    /// <summary>Log message pattern for log-based readiness check (e.g. cluster nodes).</summary>
    public string? LogPattern { get; init; }
}
