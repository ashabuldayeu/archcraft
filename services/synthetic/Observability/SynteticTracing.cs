using System.Diagnostics;

namespace SynteticApi.Observability;

public static class SynteticTracing
{
    public const string ActivitySourceName = "SynteticApi";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    public static Activity? StartPipelineActivity(string alias, string correlationId)
    {
        Activity? activity = ActivitySource.StartActivity($"pipeline.{alias}", ActivityKind.Server);
        activity?.SetTag("alias", alias);
        activity?.SetTag("correlation_id", correlationId);
        return activity;
    }

    public static Activity? StartOperationActivity(string operationType, string correlationId, Activity? parent)
    {
        Activity? activity = ActivitySource.StartActivity(
            $"operation.{operationType}",
            ActivityKind.Internal,
            parent?.Context ?? default);

        activity?.SetTag("operation_type", operationType);
        activity?.SetTag("correlation_id", correlationId);
        return activity;
    }
}
