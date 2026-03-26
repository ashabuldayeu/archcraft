namespace Archcraft.Domain.Entities;

/// <summary>A compiled connection with concrete env var name and value ready for injection.</summary>
public sealed record ResolvedConnection
{
    public required string FromService { get; init; }
    public required string ToService { get; init; }
    public required string EnvVarName { get; init; }

    /// <summary>Value injected into the From-container, e.g. "http://billing:8081" or "db:5432".</summary>
    public required string EnvVarValue { get; init; }
}
