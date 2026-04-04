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
        // Validate on original (pre-expansion) services
        ValidationResult validation = _validator.Validate(project.Topology, project.Services, project.Adapters);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                $"Project validation failed:{Environment.NewLine}{string.Join(Environment.NewLine, validation.Errors)}");

        IReadOnlyList<string> startupOrder = project.Topology.GetStartupOrder(project.Services);
        Dictionary<string, ServiceDefinition> serviceByName = project.Services.ToDictionary(s => s.Name);

        List<ServiceDefinition> orderedOriginal = startupOrder.Select(n => serviceByName[n]).ToList();

        // Expand replicas → indexed services + per-replica proxies + per-replica adapters
        (List<ServiceDefinition> expandedServices,
         List<ProxyDefinition> proxies,
         List<AdapterDefinition> expandedAdapters) = ExpandReplicas(orderedOriginal, project.Adapters.ToList());

        IReadOnlyList<ResolvedConnection> resolvedConnections = ConnectionResolver.Resolve(
            project.Topology.Connections, project.Services);

        // Inject resolved env vars — group-aware for replicated services
        List<ServiceDefinition> orderedServices = InjectEnvVars(expandedServices, resolvedConnections);

        // Inject ADAPTER_OP_*_URL — name-based lookup after expansion
        orderedServices = InjectAdapterOpUrls(orderedServices, expandedAdapters);

        // Inject SYNTETIC_CONFIG JSON
        orderedServices = InjectSynteticConfig(orderedServices);

        Dictionary<string, ServiceDefinition> serviceByNameFinal = orderedServices.ToDictionary(s => s.Name);

        List<AdapterDefinition> compiledAdapters = InjectAdapterEnvVars(expandedAdapters, serviceByNameFinal, project.Services);

        // Build proxy lookups for timeline compilation
        Dictionary<(string from, string to), List<string>> proxyByEdge =
            BuildProxyEdgeMap(proxies, project.Topology.Connections);

        Dictionary<string, string> proxyByReplica = proxies.ToDictionary(
            p => p.ProxiedService, p => p.Name, StringComparer.OrdinalIgnoreCase);

        List<TimelineScenarioDefinition> compiledTimelines = CompileTimelineScenarios(
            project.TimelineScenarios, serviceByNameFinal, proxyByEdge, proxyByReplica);

        ObservabilityDefinition? observability = project.Observability is null
            ? null
            : CompileObservability(project.Observability, orderedServices, compiledAdapters);

        return new ExecutionPlan
        {
            ProjectName = project.Name,
            OrderedServices = orderedServices,
            ResolvedConnections = resolvedConnections,
            Proxies = proxies,
            Adapters = compiledAdapters,
            Scenarios = project.Scenarios,
            TimelineScenarios = compiledTimelines,
            NetworkName = $"archcraft-{project.Name.ToLowerInvariant().Replace(' ', '-')}",
            Observability = observability
        };
    }

    // ── Replica expansion ─────────────────────────────────────────────────────

    private static (List<ServiceDefinition> services, List<ProxyDefinition> proxies, List<AdapterDefinition> adapters)
        ExpandReplicas(List<ServiceDefinition> orderedOriginal, List<AdapterDefinition> originalAdapters)
    {
        List<ServiceDefinition> services = [];
        List<ProxyDefinition> proxies = [];
        List<AdapterDefinition> adapters = [];

        Dictionary<string, AdapterDefinition> adapterByName = originalAdapters
            .ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        // Track which adapter base names were expanded to avoid double-adding them
        HashSet<string> expandedAdapterBaseNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (ServiceDefinition service in orderedOriginal)
        {
            if (service.Replicas <= 1)
            {
                services.Add(service);

                if (service.Proxy is not null)
                    proxies.Add(new ProxyDefinition
                    {
                        Name = service.Proxy,
                        ProxiedService = service.Name,
                        UpstreamHost = service.Name,
                        Port = service.Port.Value,
                        ServiceGroup = service.Name
                    });

                continue;
            }

            string? proxyBase = service.Proxy;
            expandedAdapterBaseNames.UnionWith(service.SyntheticAdapters);

            for (int i = 0; i < service.Replicas; i++)
            {
                string replicaName = $"{service.Name}-{i}";
                List<string> indexedAdapters = service.SyntheticAdapters.Select(a => $"{a}-{i}").ToList();

                services.Add(service with
                {
                    Name = replicaName,
                    Proxy = null,
                    Replicas = 1,
                    ServiceGroup = service.Name,
                    SyntheticAdapters = indexedAdapters
                });

                if (proxyBase is not null)
                    proxies.Add(new ProxyDefinition
                    {
                        Name = $"{proxyBase}-{i}",
                        ProxiedService = replicaName,
                        UpstreamHost = replicaName,
                        Port = service.Port.Value,
                        ServiceGroup = service.Name
                    });

                foreach (string adapterBaseName in service.SyntheticAdapters)
                {
                    if (adapterByName.TryGetValue(adapterBaseName, out AdapterDefinition? orig))
                        adapters.Add(orig with { Name = $"{adapterBaseName}-{i}" });
                }
            }
        }

        // Include adapters that were NOT expanded (belong to non-replicated services or infra)
        foreach (AdapterDefinition adapter in originalAdapters)
        {
            if (!expandedAdapterBaseNames.Contains(adapter.Name))
                adapters.Add(adapter);
        }

        return (services, proxies, adapters);
    }

    // ── Env var injection ─────────────────────────────────────────────────────

    private static List<ServiceDefinition> InjectEnvVars(
        List<ServiceDefinition> services,
        IReadOnlyList<ResolvedConnection> connections)
    {
        Dictionary<string, Dictionary<string, string>> injected = services
            .ToDictionary(s => s.Name, s => new Dictionary<string, string>(s.Env));

        Dictionary<string, List<string>> groupMap = BuildServiceGroupMap(services);

        foreach (ResolvedConnection connection in connections)
        {
            // Inject into either the specific service or all replicas of its group
            IEnumerable<string> targets = groupMap.TryGetValue(connection.FromService, out List<string>? replicas)
                ? replicas
                : (injected.ContainsKey(connection.FromService) ? [connection.FromService] : []);

            foreach (string target in targets)
                if (injected.TryGetValue(target, out Dictionary<string, string>? env))
                    env[connection.EnvVarName] = connection.EnvVarValue;
        }

        return services.Select(s => s with { Env = injected[s.Name] }).ToList();
    }

    private static Dictionary<string, List<string>> BuildServiceGroupMap(IEnumerable<ServiceDefinition> services)
    {
        Dictionary<string, List<string>> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (ServiceDefinition service in services)
        {
            string key = service.ServiceGroup ?? service.Name;
            if (!map.TryGetValue(key, out List<string>? list))
            {
                list = [];
                map[key] = list;
            }
            list.Add(service.Name);
        }
        return map;
    }

    // ── Adapter URL injection — name-based after expansion ────────────────────

    private static List<ServiceDefinition> InjectAdapterOpUrls(
        List<ServiceDefinition> services,
        IReadOnlyList<AdapterDefinition> adapters)
    {
        Dictionary<string, AdapterDefinition> adapterByName = adapters
            .ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> technologyToOperation = TopologyValidator.OperationTechnologyMap
            .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

        return services.Select(service =>
        {
            if (service.SyntheticAdapters.Count == 0)
                return service;

            Dictionary<string, string> env = new(service.Env);

            foreach (string adapterName in service.SyntheticAdapters)
            {
                if (!adapterByName.TryGetValue(adapterName, out AdapterDefinition? adapter)) continue;
                if (!technologyToOperation.TryGetValue(adapter.Technology, out string? operation)) continue;

                string envKey = $"ADAPTER_OP_{operation.Replace("-", "_").ToUpperInvariant()}_URL";
                env[envKey] = $"http://{adapterName}:{adapter.Port.Value}";
            }

            return service with { Env = env };
        }).ToList();
    }

    // ── Proxy edge map — returns list of proxies per edge (fan-out support) ───

    private static Dictionary<(string from, string to), List<string>> BuildProxyEdgeMap(
        List<ProxyDefinition> proxies,
        IReadOnlyList<ConnectionDefinition> connections)
    {
        Dictionary<string, List<string>> proxyNamesByGroup = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProxyDefinition proxy in proxies)
        {
            if (!proxyNamesByGroup.TryGetValue(proxy.ServiceGroup, out List<string>? list))
            {
                list = [];
                proxyNamesByGroup[proxy.ServiceGroup] = list;
            }
            list.Add(proxy.Name);
        }

        Dictionary<(string, string), List<string>> result = new();
        foreach (ConnectionDefinition conn in connections)
        {
            if (conn.Via is not null && proxyNamesByGroup.TryGetValue(conn.To, out List<string>? proxyNames))
                result[(conn.From, conn.To)] = proxyNames;
        }

        return result;
    }

    // ── Timeline compilation ──────────────────────────────────────────────────

    private static List<TimelineScenarioDefinition> CompileTimelineScenarios(
        IReadOnlyList<TimelineScenarioDefinition> scenarios,
        Dictionary<string, ServiceDefinition> services,
        Dictionary<(string from, string to), List<string>> proxyByEdge,
        Dictionary<string, string> proxyByReplica)
    {
        return scenarios.Select(scenario => scenario with
        {
            Timeline = scenario.Timeline.Select(point => point with
            {
                Actions = point.Actions
                    .Select(a => CompileAction(a, services, proxyByEdge, proxyByReplica))
                    .ToList()
            }).ToList()
        }).ToList();
    }

    private static TimelineAction CompileAction(
        TimelineAction action,
        Dictionary<string, ServiceDefinition> services,
        Dictionary<(string from, string to), List<string>> proxyByEdge,
        Dictionary<string, string> proxyByReplica)
    {
        switch (action)
        {
            case LoadAction load:
            {
                string resolvedTarget = ResolveServiceTarget(load.Target, services);
                ValidateLoadEndpoint(resolvedTarget, load.Endpoint, services);
                return load with { Target = resolvedTarget };
            }

            case InjectLatencyAction latency:
            {
                List<string> proxyNames = ResolveInjectProxies(latency.From, latency.To, proxyByEdge, proxyByReplica);
                return latency with { ProxyNames = proxyNames };
            }

            case InjectErrorAction error:
            {
                List<string> proxyNames = ResolveInjectProxies(error.From, error.To, proxyByEdge, proxyByReplica);
                return error with { ProxyNames = proxyNames };
            }

            case KillAction kill:
            {
                string replicaName = ResolveReplicaName(kill.Target, services);
                return kill with { ResolvedReplicaName = replicaName };
            }

            case RestoreAction restore:
            {
                string replicaName = ResolveReplicaName(restore.Target, services);
                return restore with { ResolvedReplicaName = replicaName };
            }

            default:
                return action;
        }
    }

    private static void ValidateLoadEndpoint(
        string resolvedTarget,
        string endpoint,
        Dictionary<string, ServiceDefinition> services)
    {
        if (string.IsNullOrEmpty(endpoint)) return;

        // For a group target, find any replica to check endpoints
        ServiceDefinition? service = services.TryGetValue(resolvedTarget, out ServiceDefinition? svc) ? svc : null;
        if (service is null)
        {
            // Group target: try finding first replica ({name}-0)
            services.TryGetValue($"{resolvedTarget}-0", out service);
        }

        if (service is not null
            && service.SyntheticEndpoints.Count > 0
            && service.SyntheticEndpoints.All(e => e.Alias != endpoint))
        {
            throw new InvalidOperationException(
                $"Endpoint '{endpoint}' not found in service '{resolvedTarget}'.");
        }
    }

    private static (string groupName, int? index) ParseReplicaTarget(string target)
    {
        int bracketIdx = target.IndexOf('[');
        if (bracketIdx < 0) return (target, null);

        string groupName = target[..bracketIdx];
        string indexPart = target[(bracketIdx + 1)..].TrimEnd(']');

        if (!int.TryParse(indexPart, out int index))
            throw new InvalidOperationException(
                $"Invalid replica index in '{target}'. Expected format: 'service[N]' where N is an integer.");

        return (groupName, index);
    }

    private static string ResolveServiceTarget(string target, Dictionary<string, ServiceDefinition> services)
    {
        (string groupName, int? index) = ParseReplicaTarget(target);
        if (index is null) return groupName;

        string replicaName = $"{groupName}-{index.Value}";
        if (!services.ContainsKey(replicaName))
            throw new InvalidOperationException(
                $"Replica '{target}' not found. Service '{groupName}' has no replica at index {index.Value}.");

        return replicaName;
    }

    private static string ResolveReplicaName(string target, Dictionary<string, ServiceDefinition> services)
    {
        (string groupName, int? index) = ParseReplicaTarget(target);

        if (index is null)
            throw new InvalidOperationException(
                $"Kill/Restore target must specify a replica index (e.g., '{target}[0]').");

        string replicaName = $"{groupName}-{index.Value}";
        if (!services.ContainsKey(replicaName))
            throw new InvalidOperationException(
                $"Replica '{target}' not found. Service '{groupName}' has no replica at index {index.Value}.");

        return replicaName;
    }

    private static List<string> ResolveInjectProxies(
        string from,
        string to,
        Dictionary<(string from, string to), List<string>> proxyByEdge,
        Dictionary<string, string> proxyByReplica)
    {
        (string toGroup, int? toIndex) = ParseReplicaTarget(to);
        (string fromGroup, _) = ParseReplicaTarget(from);

        if (!proxyByEdge.TryGetValue((fromGroup, toGroup), out List<string>? allProxies))
            throw new InvalidOperationException(
                $"No proxy found on edge '{fromGroup}' → '{toGroup}'. " +
                $"The target service '{toGroup}' must have a 'proxy:' field and a connection from '{fromGroup}' must exist.");

        if (toIndex is null)
            return allProxies; // fan-out: all proxies for the group

        string replicaService = $"{toGroup}-{toIndex.Value}";
        if (!proxyByReplica.TryGetValue(replicaService, out string? specificProxy))
            throw new InvalidOperationException(
                $"No proxy found for replica '{to}' (resolved service: '{replicaService}').");

        return [specificProxy];
    }

    // ── Other injection / compilation steps ───────────────────────────────────

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
        Dictionary<string, ServiceDefinition> services,
        IReadOnlyList<ServiceDefinition> originalServices)
    {
        // Use original services for looking up infra service config (proxy names, env vars)
        Dictionary<string, ServiceDefinition> originalByName = originalServices.ToDictionary(s => s.Name);

        return adapters.Select(adapter =>
        {
            if (adapter.ConnectsTo is null) return adapter;

            // Prefer original service definition (has Proxy and Env intact)
            Dictionary<string, ServiceDefinition> lookup = originalByName.ContainsKey(adapter.ConnectsTo)
                ? originalByName
                : services;

            if (adapter.Technology == "postgres")
            {
                string cs = BuildPgConnectionString(adapter.ConnectsTo, lookup);
                Dictionary<string, string> env = new(adapter.Env) { ["PG_CONNECTION_STRING"] = cs };
                return adapter with { Env = env };
            }

            if (adapter.Technology == "redis")
            {
                string cs = BuildRedisConnectionString(adapter.ConnectsTo, lookup);
                Dictionary<string, string> env = new(adapter.Env) { ["REDIS_CONNECTION_STRING"] = cs };
                return adapter with { Env = env };
            }

            if (adapter.Technology == "http")
            {
                string url = BuildHttpTargetUrl(adapter.ConnectsTo, lookup);
                Dictionary<string, string> env = new(adapter.Env) { ["HTTP_TARGET_URL"] = url };
                return adapter with { Env = env };
            }

            return adapter;
        }).ToList();
    }

    private static string BuildPgConnectionString(string serviceName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(serviceName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{serviceName}' not found.");

        string host = service.Proxy ?? serviceName;
        service.Env.TryGetValue("POSTGRES_DB", out string? db);
        service.Env.TryGetValue("POSTGRES_USER", out string? user);
        service.Env.TryGetValue("POSTGRES_PASSWORD", out string? password);

        return $"Host={host};Port=5432;Database={db};Username={user};Password={password}";
    }

    private static string BuildRedisConnectionString(string serviceName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(serviceName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{serviceName}' not found.");

        string host = service.Proxy ?? serviceName;
        service.Env.TryGetValue("REDIS_PASSWORD", out string? password);

        return string.IsNullOrEmpty(password) ? $"{host}:6379" : $"{host}:6379,password={password}";
    }

    private static string BuildHttpTargetUrl(string serviceName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(serviceName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{serviceName}' not found.");

        string host = service.Proxy ?? serviceName;
        return $"http://{host}:{service.Port.Value}";
    }

    // ── Observability compilation ─────────────────────────────────────────────

    private static ObservabilityDefinition CompileObservability(
        ObservabilityDefinition observability,
        IReadOnlyList<ServiceDefinition> services,
        IReadOnlyList<AdapterDefinition> adapters)
    {
        WarnIfUnsupportedVersion(observability.Prometheus.Image, "Prometheus", new Version(2, 40, 0));
        WarnIfUnsupportedVersion(observability.Grafana.Image, "Grafana", new Version(10, 0, 0));

        // Deduplicate exporters by ConnectsTo service (avoid one exporter per replica-adapter)
        HashSet<string> seenServices = new(StringComparer.OrdinalIgnoreCase);
        List<ExporterDefinition> exporters = [];

        // Use original service definitions (group roots) by looking at original services
        Dictionary<string, ServiceDefinition> serviceByName = services.ToDictionary(s => s.Name);

        foreach (AdapterDefinition adapter in adapters)
        {
            if (adapter.ConnectsTo is null || !seenServices.Add(adapter.ConnectsTo)) continue;
            if (!serviceByName.TryGetValue(adapter.ConnectsTo, out ServiceDefinition? service)) continue;

            ExporterDefinition? exporter = adapter.Technology.ToLowerInvariant() switch
            {
                "redis" => BuildRedisExporter(service),
                "postgres" => BuildPostgresExporter(service),
                _ => null
            };

            if (exporter is not null)
                exporters.Add(exporter);
        }

        return observability with { Exporters = exporters };
    }

    private static ExporterDefinition BuildRedisExporter(ServiceDefinition service) =>
        new()
        {
            Name = $"{service.Name}-exporter",
            Image = "oliver006/redis_exporter:v1.67.0",
            ServiceName = service.Name,
            Technology = "redis",
            ExporterPort = 9121,
            Env = new Dictionary<string, string>
            {
                ["REDIS_ADDR"] = $"redis://{service.Name}:{service.Port.Value}"
            }
        };

    private static ExporterDefinition BuildPostgresExporter(ServiceDefinition service)
    {
        service.Env.TryGetValue("POSTGRES_DB", out string? db);
        service.Env.TryGetValue("POSTGRES_USER", out string? user);
        service.Env.TryGetValue("POSTGRES_PASSWORD", out string? password);

        return new()
        {
            Name = $"{service.Name}-exporter",
            Image = "prometheuscommunity/postgres-exporter:v0.16.0",
            ServiceName = service.Name,
            Technology = "postgres",
            ExporterPort = 9187,
            Env = new Dictionary<string, string>
            {
                ["DATA_SOURCE_NAME"] = $"postgresql://{user}:{password}@{service.Name}:{service.Port.Value}/{db}?sslmode=disable"
            }
        };
    }

    private static void WarnIfUnsupportedVersion(string image, string name, Version minVersion)
    {
        int colonIndex = image.LastIndexOf(':');
        if (colonIndex < 0) return;

        string tag = image[(colonIndex + 1)..].TrimStart('v');
        if (!Version.TryParse(tag, out Version? version)) return;

        if (version < minVersion)
            Console.Error.WriteLine(
                $"[WARNING] {name} image '{image}' is below the minimum supported version {minVersion}. Some features may not work correctly.");
    }
}
