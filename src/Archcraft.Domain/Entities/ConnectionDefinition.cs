using Archcraft.Domain.Enums;

namespace Archcraft.Domain.Entities;

public sealed record ConnectionDefinition
{
    public required string From { get; init; }
    public required string To { get; init; }
    public ConnectionProtocol Protocol { get; init; } = ConnectionProtocol.Http;

    /// <summary>Port on the To-service. 0 means use the service's declared port.</summary>
    public int Port { get; init; } = 0;

    /// <summary>Env var name injected into From-container. Null = auto-generate as {TO_UPPER}_URL.</summary>
    public string? Alias { get; init; }

    /// <summary>Future chaos injection point. Parsed but unused in MVP.</summary>
    public string? Via { get; init; }
}
