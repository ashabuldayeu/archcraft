using Archcraft.Domain.Enums;
using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public sealed record ScenarioDefinition
{
    public required string Name { get; init; }
    public required ScenarioType Type { get; init; }
    public required string Target { get; init; }
    public required RpsTarget Rps { get; init; }
    public required Duration ScenarioDuration { get; init; }
    public Duration StartupTimeout { get; init; } = Duration.Parse("30s");
}
