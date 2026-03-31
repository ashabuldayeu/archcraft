namespace Archcraft.Domain.Entities;

public sealed record ProxyDefinition
{
    public required string Name { get; init; }
    public required string ProxiedService { get; init; }
    public required string UpstreamHost { get; init; }
    public required int Port { get; init; }
}
