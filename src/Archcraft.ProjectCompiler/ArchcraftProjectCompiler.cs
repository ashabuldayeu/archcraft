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
        ValidationResult validation = _validator.Validate(project.Topology, project.Services, project.Adapters);
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

        Dictionary<string, ServiceDefinition> serviceByNameFinal = orderedServices.ToDictionary(s => s.Name);
        List<AdapterDefinition> compiledAdapters = InjectAdapterEnvVars(project.Adapters, serviceByNameFinal);

        return new ExecutionPlan
        {
            ProjectName = project.Name,
            OrderedServices = orderedServices,
            ResolvedConnections = resolvedConnections,
            Adapters = compiledAdapters,
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

    private static List<AdapterDefinition> InjectAdapterEnvVars(
        IReadOnlyList<AdapterDefinition> adapters,
        Dictionary<string, ServiceDefinition> services)
    {
        return adapters.Select(adapter =>
        {
            if (adapter.Technology == "postgres" && adapter.ConnectsTo is not null)
            {
                string connectionString = BuildPgConnectionString(adapter.ConnectsTo, services);
                Dictionary<string, string> env = new(adapter.Env) { ["PG_CONNECTION_STRING"] = connectionString };
                return adapter with { Env = env };
            }

            if (adapter.Technology == "redis" && adapter.ConnectsTo is not null)
            {
                string connectionString = BuildRedisConnectionString(adapter.ConnectsTo, services);
                Dictionary<string, string> env = new(adapter.Env) { ["REDIS_CONNECTION_STRING"] = connectionString };
                return adapter with { Env = env };
            }

            return adapter;
        }).ToList();
    }

    private static string BuildPgConnectionString(string serviceName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(serviceName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{serviceName}' not found in project.");

        service.Env.TryGetValue("POSTGRES_DB", out string? db);
        service.Env.TryGetValue("POSTGRES_USER", out string? user);
        service.Env.TryGetValue("POSTGRES_PASSWORD", out string? password);

        return $"Host={serviceName};Port=5432;Database={db};Username={user};Password={password}";
    }

    private static string BuildRedisConnectionString(string serviceName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(serviceName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{serviceName}' not found in project.");

        service.Env.TryGetValue("REDIS_PASSWORD", out string? password);

        return string.IsNullOrEmpty(password)
            ? $"{serviceName}:6379"
            : $"{serviceName}:6379,password={password}";
    }
}
