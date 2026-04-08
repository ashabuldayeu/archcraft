using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public sealed record ServiceDefinition
{
    public required string Name { get; init; }
    public required string Image { get; init; }
    public required ServicePort Port { get; init; }
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
    public ReadinessConfig? Readiness { get; init; }
    public string? Proxy { get; init; }
    public IReadOnlyList<string> SyntheticAdapters { get; init; } = [];
    public IReadOnlyList<string> SyntheticOperations { get; init; } = [];
    public IReadOnlyList<SyntheticEndpoint> SyntheticEndpoints { get; init; } = [];
    public int Replicas { get; init; } = 1;
    /// <summary>Original service group name before replica expansion. Null for non-replicated services.</summary>
    public string? ServiceGroup { get; init; }
    public ClusterDefinition? Cluster { get; init; }
}
