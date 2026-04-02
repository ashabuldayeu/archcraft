using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public sealed record TimelinePoint
{
    public required TimeSpan At { get; init; }
    public IReadOnlyList<TimelineAction> Actions { get; init; } = [];
}
