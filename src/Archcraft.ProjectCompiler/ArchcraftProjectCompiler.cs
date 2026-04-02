using System.Text.Json;
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

        // Inject ADAPTER_OP_*_URL env vars into synthetic services
        Dictionary<string, AdapterDefinition> adapterByTechnology = project.Adapters
            .ToDictionary(a => a.Technology, StringComparer.OrdinalIgnoreCase);
        orderedServices = InjectAdapterOpUrls(orderedServices, adapterByTechnology);

        // Inject SYNTETIC_CONFIG JSON into synthetic services
        orderedServices = InjectSynteticConfig(orderedServices);

        Dictionary<string, ServiceDefinition> serviceByNameFinal = orderedServices.ToDictionary(s => s.Name);

        List<ProxyDefinition> proxies = BuildProxies(serviceByNameFinal);
        List<AdapterDefinition> compiledAdapters = InjectAdapterEnvVars(project.Adapters, serviceByNameFinal);

        // Build proxy lookup: (from, to) → proxy name
        Dictionary<(string from, string to), string> proxyByEdge = BuildProxyEdgeMap(
            proxies, project.Topology.Connections);

        List<TimelineScenarioDefinition> compiledTimelines = CompileTimelineScenarios(
            project.TimelineScenarios, serviceByNameFinal, proxyByEdge);

        return new ExecutionPlan
        {
            ProjectName = project.Name,
            OrderedServices = orderedServices,
            ResolvedConnections = resolvedConnections,
            Proxies = proxies,
            Adapters = compiledAdapters,
            Scenarios = project.Scenarios,
            TimelineScenarios = compiledTimelines,
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

    private static List<ServiceDefinition> InjectAdapterOpUrls(
        List<ServiceDefinition> services,
        Dictionary<string, AdapterDefinition> adapterByTechnology)
    {
        return services.Select(service =>
        {
            if (service.SyntheticOperations.Count == 0)
                return service;

            Dictionary<string, string> env = new(service.Env);

            foreach (string operation in service.SyntheticOperations)
            {
                if (!TopologyValidator.OperationTechnologyMap.TryGetValue(operation, out string? technology))
                    continue;

                if (!adapterByTechnology.TryGetValue(technology, out AdapterDefinition? adapter))
                    continue;

                string envKey = $"ADAPTER_OP_{operation.Replace("-", "_").ToUpperInvariant()}_URL";
                env[envKey] = $"http://{adapter.Name}:{adapter.Port.Value}";
            }

            return service with { Env = env };
        }).ToList();
    }

    private static List<ProxyDefinition> BuildProxies(Dictionary<string, ServiceDefinition> services)
    {
        List<ProxyDefinition> proxies = [];
        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (ServiceDefinition service in services.Values)
        {
            if (service.Proxy is null)
                continue;

            if (!seenNames.Add(service.Proxy))
                throw new InvalidOperationException(
                    $"Duplicate proxy name '{service.Proxy}'. Each proxy name must be unique.");

            proxies.Add(new ProxyDefinition
            {
                Name = service.Proxy,
                ProxiedService = service.Name,
                UpstreamHost = service.Name,
                Port = service.Port.Value
            });
        }

        return proxies;
    }

    private static List<ServiceDefinition> InjectSynteticConfig(List<ServiceDefinition> services)
    {
        return services.Select(service =>
        {
            if (service.SyntheticEndpoints.Count == 0)
                return service;

            object config = new
            {
                ServiceName = service.Name,
                Endpoints = service.SyntheticEndpoints.Select(BuildEndpointDto).ToList()
            };

            string json = JsonSerializer.Serialize(config);
            Dictionary<string, string> env = new(service.Env) { ["SYNTETIC_CONFIG"] = json };
            return service with { Env = env };
        }).ToList();
    }

    private static object BuildEndpointDto(SyntheticEndpoint endpoint) => new
    {
        endpoint.Alias,
        Pipeline = endpoint.Pipeline.Select(BuildStepDto).ToList()
    };

    private static object BuildStepDto(PipelineStep step) => new
    {
        step.Operation,
        step.NotFoundRate,
        Fallback = step.Fallback.Select(BuildStepDto).ToList(),
        Children = step.Children.Select(BuildStepDto).ToList()
    };

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

            if (adapter.Technology == "http" && adapter.ConnectsTo is not null)
            {
                string targetUrl = BuildHttpTargetUrl(adapter.ConnectsTo, services);
                Dictionary<string, string> env = new(adapter.Env) { ["HTTP_TARGET_URL"] = targetUrl };
                return adapter with { Env = env };
            }

            return adapter;
        }).ToList();
    }

    private static string BuildPgConnectionString(string serviceName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(serviceName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{serviceName}' not found in project.");

        string host = service.Proxy ?? serviceName;
        service.Env.TryGetValue("POSTGRES_DB", out string? db);
        service.Env.TryGetValue("POSTGRES_USER", out string? user);
        service.Env.TryGetValue("POSTGRES_PASSWORD", out string? password);

        return $"Host={host};Port=5432;Database={db};Username={user};Password={password}";
    }

    private static string BuildRedisConnectionString(string serviceName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(serviceName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{serviceName}' not found in project.");

        string host = service.Proxy ?? serviceName;
        service.Env.TryGetValue("REDIS_PASSWORD", out string? password);

        return string.IsNullOrEmpty(password)
            ? $"{host}:6379"
            : $"{host}:6379,password={password}";
    }

    private static string BuildHttpTargetUrl(string serviceName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(serviceName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{serviceName}' not found in project.");

        string host = service.Proxy ?? serviceName;
        return $"http://{host}:{service.Port.Value}";
    }

    private static Dictionary<(string from, string to), string> BuildProxyEdgeMap(
        List<ProxyDefinition> proxies,
        IReadOnlyList<ConnectionDefinition> connections)
    {
        Dictionary<string, string> proxyByService = proxies
            .ToDictionary(p => p.ProxiedService, p => p.Name, StringComparer.OrdinalIgnoreCase);

        Dictionary<(string, string), string> result = new();

        foreach (ConnectionDefinition conn in connections)
        {
            if (conn.Via is not null && proxyByService.TryGetValue(conn.To, out string? proxyName))
                result[(conn.From, conn.To)] = proxyName;
        }

        return result;
    }

    private static List<TimelineScenarioDefinition> CompileTimelineScenarios(
        IReadOnlyList<TimelineScenarioDefinition> scenarios,
        Dictionary<string, ServiceDefinition> services,
        Dictionary<(string from, string to), string> proxyByEdge)
    {
        return scenarios.Select(scenario => scenario with
        {
            Timeline = scenario.Timeline.Select(point => point with
            {
                Actions = point.Actions.Select(action => CompileAction(action, services, proxyByEdge)).ToList()
            }).ToList()
        }).ToList();
    }

    private static TimelineAction CompileAction(
        TimelineAction action,
        Dictionary<string, ServiceDefinition> services,
        Dictionary<(string from, string to), string> proxyByEdge)
    {
        if (action is LoadAction load)
        {
            if (!services.TryGetValue(load.Target, out ServiceDefinition? service))
                throw new InvalidOperationException(
                    $"Timeline load target '{load.Target}' not found in project.");

            if (service.SyntheticEndpoints.Count == 0)
                throw new InvalidOperationException(
                    $"Timeline load target '{load.Target}' is not a synthetic service. Only synthetic services can be load targets.");

            if (!string.IsNullOrEmpty(load.Endpoint)
                && service.SyntheticEndpoints.All(e => e.Alias != load.Endpoint))
                throw new InvalidOperationException(
                    $"Endpoint '{load.Endpoint}' not found in synthetic service '{load.Target}'.");

            return load;
        }

        if (action is InjectLatencyAction latency)
        {
            string proxyName = ResolveInjectProxy(latency.From, latency.To, proxyByEdge);
            return latency with { ProxyName = proxyName };
        }

        if (action is InjectErrorAction error)
        {
            string proxyName = ResolveInjectProxy(error.From, error.To, proxyByEdge);
            return error with { ProxyName = proxyName };
        }

        return action;
    }

    private static string ResolveInjectProxy(
        string from, string to,
        Dictionary<(string, string), string> proxyByEdge)
    {
        if (proxyByEdge.TryGetValue((from, to), out string? proxyName))
            return proxyName;

        throw new InvalidOperationException(
            $"No proxy found between '{from}' and '{to}'. " +
            $"The target service '{to}' must have a 'proxy:' field and a connection from '{from}' to '{to}' must exist.");
    }
}
