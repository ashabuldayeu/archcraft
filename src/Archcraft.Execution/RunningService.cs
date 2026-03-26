namespace Archcraft.Execution;

public sealed record RunningService
{
    public required string Name { get; init; }
    public required string Host { get; init; }
    public required int MappedPort { get; init; }

    public string Address => $"{Host}:{MappedPort}";
}
