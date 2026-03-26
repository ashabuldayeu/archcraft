namespace SynteticApi.Operations;

public enum OperationOutcome
{
    Success,
    NotFound,
    Error
}

public sealed class OperationResult
{
    public required OperationOutcome Outcome { get; init; }
    public TimeSpan Duration { get; init; }

    public static OperationResult Success(TimeSpan duration) =>
        new() { Outcome = OperationOutcome.Success, Duration = duration };

    public static OperationResult NotFound(TimeSpan duration) =>
        new() { Outcome = OperationOutcome.NotFound, Duration = duration };

    public static OperationResult Error(TimeSpan duration) =>
        new() { Outcome = OperationOutcome.Error, Duration = duration };
}
