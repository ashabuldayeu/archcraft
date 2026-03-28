using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public sealed record AdapterDefinition
{
    public required string Name { get; init; }
    public required string Image { get; init; }
    public required ServicePort Port { get; init; }
    public required string Technology { get; init; }
    public string? ConnectsTo { get; init; }
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
}
