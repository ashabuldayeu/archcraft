using Archcraft.Contracts;
using Archcraft.Domain.Entities;

namespace Archcraft.ProjectCompiler;

public sealed class TopologyValidator : ITopologyValidator
{
    internal static readonly IReadOnlyDictionary<string, string> OperationTechnologyMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["redis-call"] = "redis",
            ["pg-call"]    = "postgres",
            ["http-call"]  = "http"
        };

    public ValidationResult Validate(
        ServiceTopology topology,
        IReadOnlyList<ServiceDefinition> services,
        IReadOnlyList<AdapterDefinition> adapters)
    {
        List<string> errors = [];

        ValidateConnections(topology, services, errors);
        ValidateAdapterRefs(services, adapters, errors);
        ValidateOperationTechnologies(services, adapters, errors);

        if (errors.Count > 0)
            return ValidationResult.Failure(errors);

        try
        {
            topology.GetStartupOrder(services);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }

    private static void ValidateConnections(
        ServiceTopology topology,
        IReadOnlyList<ServiceDefinition> services,
        List<string> errors)
    {
        HashSet<string> knownNames = services.Select(s => s.Name).ToHashSet();

        foreach (ConnectionDefinition connection in topology.Connections)
        {
            if (!knownNames.Contains(connection.From))
                errors.Add($"Connection references unknown service '{connection.From}' in 'from'.");

            if (!knownNames.Contains(connection.To))
                errors.Add($"Connection references unknown service '{connection.To}' in 'to'.");

            if (connection.From == connection.To)
                errors.Add($"Connection from '{connection.From}' to itself is not allowed.");
        }
    }

    private static void ValidateAdapterRefs(
        IReadOnlyList<ServiceDefinition> services,
        IReadOnlyList<AdapterDefinition> adapters,
        List<string> errors)
    {
        HashSet<string> adapterNames = adapters.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (ServiceDefinition service in services)
        {
            foreach (string adapterRef in service.SyntheticAdapters)
            {
                if (!adapterNames.Contains(adapterRef))
                    errors.Add(
                        $"Service '{service.Name}' references adapter '{adapterRef}' which is not declared in 'adapters:'.");
            }
        }
    }

    private static void ValidateOperationTechnologies(
        IReadOnlyList<ServiceDefinition> services,
        IReadOnlyList<AdapterDefinition> adapters,
        List<string> errors)
    {
        HashSet<string> registeredTechnologies = adapters
            .Select(a => a.Technology)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (ServiceDefinition service in services)
        {
            foreach (string operation in service.SyntheticOperations)
            {
                if (!OperationTechnologyMap.TryGetValue(operation, out string? requiredTechnology))
                    continue;

                if (!registeredTechnologies.Contains(requiredTechnology))
                    errors.Add(
                        $"Service '{service.Name}' uses operation '{operation}' which requires technology " +
                        $"'{requiredTechnology}', but no adapter with that technology is registered.");
            }
        }
    }
}
