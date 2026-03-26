using SynteticApi.Operations;

namespace SynteticApi.Pipeline;

public sealed class PipelineStepResult
{
    public required string Operation { get; init; }
    public required OperationOutcome Outcome { get; init; }
    public required TimeSpan Duration { get; init; }
    public bool FallbackExecuted { get; init; }
    public List<PipelineStepResult> Children { get; init; } = [];
}
