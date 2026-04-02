namespace Archcraft.Domain.Entities;

public sealed record ProjectDefinition
{
    public required string Name { get; init; }
    public required IReadOnlyList<AdapterDefinition> Adapters { get; init; }
    public required IReadOnlyList<ServiceDefinition> Services { get; init; }
    public required ServiceTopology Topology { get; init; }
    public required IReadOnlyList<ScenarioDefinition> Scenarios { get; init; }
    public IReadOnlyList<TimelineScenarioDefinition> TimelineScenarios { get; init; } = [];
}
