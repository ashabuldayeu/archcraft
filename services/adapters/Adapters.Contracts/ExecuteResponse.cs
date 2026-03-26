namespace Adapters.Contracts;

public sealed class ExecuteResponse
{
    public required AdapterOutcome Outcome { get; init; }
    public required long DurationMs { get; init; }
    public Dictionary<string, object?> Data { get; init; } = [];

    public static ExecuteResponse Success(long durationMs) =>
        new() { Outcome = AdapterOutcome.Success, DurationMs = durationMs };

    public static ExecuteResponse NotFound(long durationMs) =>
        new() { Outcome = AdapterOutcome.NotFound, DurationMs = durationMs };

    public static ExecuteResponse Error(long durationMs) =>
        new() { Outcome = AdapterOutcome.Error, DurationMs = durationMs };
}
