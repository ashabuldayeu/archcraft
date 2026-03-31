namespace Archcraft.Execution;

public sealed record RunningProxy
{
    public required string Name { get; init; }
    public required string ProxiedService { get; init; }
    public required string ApiUrl { get; init; }
    public required int ListenPort { get; init; }
}
