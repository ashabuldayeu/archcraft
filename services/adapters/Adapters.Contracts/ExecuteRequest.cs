namespace Adapters.Contracts;

public sealed class ExecuteRequest
{
    public required string Operation { get; init; }
    public Dictionary<string, object?> Payload { get; init; } = [];
}
