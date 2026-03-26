using System.Diagnostics;
using SynteticApi.Configuration;
using SynteticApi.Observability;
using SynteticApi.Operations;
using SynteticApi.Pipeline;

namespace SynteticApi.Endpoints;

public static class PipelineEndpoints
{
    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/{alias}", async (
            string alias,
            HttpContext httpContext,
            RuntimeConfigStore store,
            PipelineExecutor executor,
            SynteticMetrics metrics,
            ILogger<PipelineExecutor> logger,
            CancellationToken cancellationToken) =>
        {
            EndpointOptions? endpoint = store.GetEndpoint(alias);

            if (endpoint is null)
                return Results.NotFound(new { error = $"Alias '{alias}' not found." });

            string correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                ?? Guid.NewGuid().ToString();

            httpContext.Response.Headers["X-Correlation-Id"] = correlationId;

            Stopwatch sw = Stopwatch.StartNew();

            using Activity? pipelineActivity = SynteticTracing.StartPipelineActivity(alias, correlationId);

            OperationContext context = new()
            {
                CorrelationId = correlationId,
                ParentActivity = pipelineActivity
            };

            List<PipelineStepResult> results = await executor.ExecuteAsync(
                endpoint.Pipeline, context, cancellationToken);

            sw.Stop();

            metrics.HttpRequestDuration.Record(
                sw.Elapsed.TotalSeconds,
                new TagList { { "alias", alias }, { "http.method", "POST" } });

            return Results.Ok(new
            {
                correlationId,
                alias,
                steps = results
            });
        })
        .WithName("ExecutePipeline");

        return app;
    }
}
