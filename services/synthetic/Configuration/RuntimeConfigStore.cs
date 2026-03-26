using System.Collections.Concurrent;

namespace SynteticApi.Configuration;

public sealed class RuntimeConfigStore
{
    private readonly ConcurrentDictionary<string, EndpointOptions> _endpoints = new(StringComparer.OrdinalIgnoreCase);

    public void Initialize(IEnumerable<EndpointOptions> endpoints)
    {
        foreach (EndpointOptions endpoint in endpoints)
            _endpoints[endpoint.Alias] = endpoint;
    }

    public EndpointOptions? GetEndpoint(string alias)
        => _endpoints.TryGetValue(alias, out EndpointOptions? endpoint) ? endpoint : null;

    public IReadOnlyDictionary<string, EndpointOptions> GetAll()
        => _endpoints;

    public bool ApplyPatch(ConfigPatchRequest patch)
    {
        foreach ((string alias, EndpointPatch endpointPatch) in patch.Endpoints)
        {
            if (!_endpoints.TryGetValue(alias, out EndpointOptions? endpoint))
                return false;

            ApplyStepPatches(endpoint.Pipeline, endpointPatch.Steps);
        }

        return true;
    }

    private static void ApplyStepPatches(List<PipelineStepOptions> steps, Dictionary<string, StepPatch> patches)
    {
        foreach (PipelineStepOptions step in steps)
        {
            if (patches.TryGetValue(step.Operation, out StepPatch? patch))
            {
                if (patch.NotFoundRate.HasValue)
                    step.NotFoundRate = patch.NotFoundRate.Value;
            }

            ApplyStepPatches(step.Fallback, patches);
            ApplyStepPatches(step.Children, patches);
        }
    }
}

public sealed class ConfigPatchRequest
{
    public Dictionary<string, EndpointPatch> Endpoints { get; set; } = [];
}

public sealed class EndpointPatch
{
    public Dictionary<string, StepPatch> Steps { get; set; } = [];
}

public sealed class StepPatch
{
    public double? NotFoundRate { get; set; }
}
