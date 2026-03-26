using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public sealed record ReadinessConfig
{
    public required string Path { get; init; }
    public required Duration Timeout { get; init; }
}
