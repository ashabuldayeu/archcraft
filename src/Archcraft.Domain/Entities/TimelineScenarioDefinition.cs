using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public sealed record TimelineScenarioDefinition
{
    public required string Name { get; init; }
    public Duration StartupTimeout { get; init; } = Duration.Parse("30s");
    public IReadOnlyList<TimelinePoint> Timeline { get; init; } = [];
}
