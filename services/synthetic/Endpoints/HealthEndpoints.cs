namespace SynteticApi.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .WithName("Health")
            .ExcludeFromDescription();

        return app;
    }
}
