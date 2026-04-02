using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public abstract record TimelineAction
{
    public Duration? Duration { get; init; }
}

public sealed record LoadAction : TimelineAction
{
    public required string Target { get; init; }
    public required string Endpoint { get; init; }
    public required int Rps { get; init; }
}

public sealed record InjectLatencyAction : TimelineAction
{
    public required string From { get; init; }
    public required string To { get; init; }
    public string? ProxyName { get; init; }
    public required int LatencyMs { get; init; }
}

public sealed record InjectErrorAction : TimelineAction
{
    public required string From { get; init; }
    public required string To { get; init; }
    public string? ProxyName { get; init; }
    public required double ErrorRate { get; init; }
}
