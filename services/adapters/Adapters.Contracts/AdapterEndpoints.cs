using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Adapters.Contracts;

public static class AdapterEndpoints
{
    public static IEndpointRouteBuilder MapAdapterEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/execute", async (
            ExecuteRequest request,
            HttpContext httpContext,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
        {
            string correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                ?? string.Empty;

            httpContext.Response.Headers["X-Correlation-Id"] = correlationId;

            request.CorrelationId = string.IsNullOrEmpty(correlationId) ? null : correlationId;

            IEnumerable<IAdapterOperation> operations = services.GetServices<IAdapterOperation>();
            IAdapterOperation? operation = operations
                .FirstOrDefault(o => o.OperationName.Equals(request.Operation, StringComparison.OrdinalIgnoreCase));

            if (operation is null)
                return Results.BadRequest(new ExecuteResponse
                {
                    Outcome = AdapterOutcome.Error,
                    DurationMs = 0,
                    Data = new Dictionary<string, object?> { ["error"] = $"Unknown operation '{request.Operation}'." }
                });

            ExecuteResponse response = await operation.ExecuteAsync(request, cancellationToken);
            return Results.Ok(response);
        })
        .WithName("Execute");

        return app;
    }
}
