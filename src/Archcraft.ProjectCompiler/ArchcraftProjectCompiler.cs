using Archcraft.Contracts;
using Archcraft.Domain.Entities;

namespace Archcraft.ProjectCompiler;

public sealed class ArchcraftProjectCompiler : IProjectCompiler
{
    private readonly ITopologyValidator _validator;

    public ArchcraftProjectCompiler(ITopologyValidator validator)
    {
        _validator = validator;
    }

    public ExecutionPlan Compile(ProjectDefinition project)
    {
        ValidationResult validation = _validator.Validate(project.Topology, project.Services);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                $"Project validation failed:{Environment.NewLine}{string.Join(Environment.NewLine, validation.Errors)}");

        IReadOnlyList<string> startupOrder = project.Topology.GetStartupOrder(project.Services);
        Dictionary<string, ServiceDefinition> serviceByName = project.Services.ToDictionary(s => s.Name);

        List<ServiceDefinition> orderedServices = startupOrder
            .Select(name => serviceByName[name])
            .ToList();

        IReadOnlyList<ResolvedConnection> resolvedConnections = ConnectionResolver.Resolve(
            project.Topology.Connections,
            project.Services);

        // Inject resolved env vars into the ordered service definitions
        orderedServices = InjectEnvVars(orderedServices, resolvedConnections);

        return new ExecutionPlan
        {
            ProjectName = project.Name,
            OrderedServices = orderedServices,
            ResolvedConnections = resolvedConnections,
            Scenarios = project.Scenarios,
            NetworkName = $"archcraft-{project.Name.ToLowerInvariant().Replace(' ', '-')}"
        };
    }

    private static List<ServiceDefinition> InjectEnvVars(
        List<ServiceDefinition> services,
        IReadOnlyList<ResolvedConnection> connections)
    {
        Dictionary<string, Dictionary<string, string>> injected = services
            .ToDictionary(s => s.Name, s => new Dictionary<string, string>(s.Env));

        foreach (ResolvedConnection connection in connections)
        {
            if (injected.TryGetValue(connection.FromService, out Dictionary<string, string>? env))
                env[connection.EnvVarName] = connection.EnvVarValue;
        }

        return services
            .Select(s => s with { Env = injected[s.Name] })
            .ToList();
    }
}
