using System.Diagnostics;
using SynteticApi.Configuration;
using SynteticApi.Observability;
using SynteticApi.Operations;

namespace SynteticApi.Pipeline;

public sealed class PipelineExecutor
{
    private static readonly Random _random = Random.Shared;

    private readonly AdapterCallOperation _adapterCall;
    private readonly SynteticMetrics _metrics;
    private readonly ILogger<PipelineExecutor> _logger;

    public PipelineExecutor(
        AdapterCallOperation adapterCall,
        SynteticMetrics metrics,
        ILogger<PipelineExecutor> logger)
    {
        _adapterCall = adapterCall;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<List<PipelineStepResult>> ExecuteAsync(
        IEnumerable<PipelineStepOptions> steps,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        List<PipelineStepResult> results = [];

        foreach (PipelineStepOptions step in steps)
        {
            PipelineStepResult result = await ExecuteStepAsync(step, context, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    private async Task<PipelineStepResult> ExecuteStepAsync(
        PipelineStepOptions step,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        using Activity? activity = SynteticTracing.StartOperationActivity(
            step.Operation, context.CorrelationId, context.ParentActivity);

        OperationResult opResult = await _adapterCall.ExecuteAsync(step.Operation, context, cancellationToken);

        OperationOutcome outcome = ResolveOutcome(step.NotFoundRate, opResult.Outcome);

        activity?.SetTag("result", outcome.ToString().ToLowerInvariant());

        RecordMetrics(step.Operation, outcome, opResult.Duration);

        _logger.LogDebug(
            "Operation {Operation} completed with outcome {Outcome} in {Duration}ms (correlation_id={CorrelationId})",
            step.Operation, outcome, opResult.Duration.TotalMilliseconds, context.CorrelationId);

        bool fallbackExecuted = false;
        List<PipelineStepResult> childResults = [];

        if (outcome == OperationOutcome.NotFound && step.Fallback.Count > 0)
        {
            fallbackExecuted = true;
            childResults = await ExecuteAsync(step.Fallback, context, cancellationToken);
        }
        else if (outcome == OperationOutcome.Success && step.Children.Count > 0)
        {
            childResults = await ExecuteAsync(step.Children, context, cancellationToken);
        }

        return new PipelineStepResult
        {
            Operation = step.Operation,
            Outcome = outcome,
            Duration = opResult.Duration,
            FallbackExecuted = fallbackExecuted,
            Children = childResults
        };
    }

    private static OperationOutcome ResolveOutcome(double notFoundRate, OperationOutcome baseOutcome)
    {
        if (baseOutcome == OperationOutcome.Error)
            return OperationOutcome.Error;

        if (notFoundRate > 0 && _random.NextDouble() < notFoundRate)
            return OperationOutcome.NotFound;

        return OperationOutcome.Success;
    }

    private void RecordMetrics(string operationType, OperationOutcome outcome, TimeSpan duration)
    {
        TagList tags = new()
        {
            { "operation_type", operationType },
            { "result", outcome.ToString().ToLowerInvariant() }
        };

        _metrics.OperationDuration.Record(duration.TotalSeconds, tags);
        _metrics.OperationCallsTotal.Add(1, tags);
    }
}
