using SynteticApi.Configuration;

namespace SynteticApi.Endpoints;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/config", (RuntimeConfigStore store) =>
            Results.Ok(store.GetAll()))
            .WithName("GetConfig");

        app.MapPatch("/config", (ConfigPatchRequest patch, RuntimeConfigStore store) =>
        {
            if (patch.Endpoints.Count == 0)
                return Results.BadRequest(new { error = "Patch must contain at least one endpoint entry." });

            foreach ((string alias, EndpointPatch endpointPatch) in patch.Endpoints)
            {
                foreach ((string stepName, StepPatch stepPatch) in endpointPatch.Steps)
                {
                    if (stepPatch.NotFoundRate.HasValue && (stepPatch.NotFoundRate < 0 || stepPatch.NotFoundRate > 1))
                        return Results.BadRequest(new { error = $"not-found-rate for step '{stepName}' must be between 0 and 1." });
                }
            }

            bool applied = store.ApplyPatch(patch);

            if (!applied)
                return Results.BadRequest(new { error = "One or more specified aliases were not found in the current configuration." });

            return Results.Ok(store.GetAll());
        })
        .WithName("PatchConfig");

        return app;
    }
}
