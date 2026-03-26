using Archcraft.Contracts;
using Archcraft.Domain.Entities;

namespace Archcraft.ProjectCompiler;

public sealed class TopologyValidator : ITopologyValidator
{
    public ValidationResult Validate(ServiceTopology topology, IReadOnlyList<ServiceDefinition> services)
    {
        HashSet<string> knownNames = services.Select(s => s.Name).ToHashSet();
        List<string> errors = [];

        foreach (ConnectionDefinition connection in topology.Connections)
        {
            if (!knownNames.Contains(connection.From))
                errors.Add($"Connection references unknown service '{connection.From}' in 'from'.");

            if (!knownNames.Contains(connection.To))
                errors.Add($"Connection references unknown service '{connection.To}' in 'to'.");

            if (connection.From == connection.To)
                errors.Add($"Connection from '{connection.From}' to itself is not allowed.");
        }

        if (errors.Count > 0)
            return ValidationResult.Failure(errors);

        // Detect cycles via topological sort
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
}
