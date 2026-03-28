namespace Archcraft.Domain.Entities;

public sealed record ExecutionPlan
{
    public required string ProjectName { get; init; }

    /// <summary>Services in startup order (dependencies first).</summary>
    public required IReadOnlyList<ServiceDefinition> OrderedServices { get; init; }

    /// <summary>Compiled connections with resolved env var names and values.</summary>
    public required IReadOnlyList<ResolvedConnection> ResolvedConnections { get; init; }

    public required IReadOnlyList<AdapterDefinition> Adapters { get; init; }

    public required IReadOnlyList<ScenarioDefinition> Scenarios { get; init; }

    public required string NetworkName { get; init; }
}
