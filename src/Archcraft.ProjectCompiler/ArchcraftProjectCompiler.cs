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

        // Expand clusters → primary + replica nodes with Bitnami env vars
        (List<ServiceDefinition> clusteredServices,
         Dictionary<string, string> clusterPrimaryMap) = ExpandClusters(expandedServices, project.Adapters);

        // Update proxy upstream hosts: e.g. "postgres" → "postgres-primary"
        proxies = proxies
            .Select(p => clusterPrimaryMap.TryGetValue(p.UpstreamHost, out string? primaryHost)
                ? p with { UpstreamHost = primaryHost, ProxiedService = primaryHost }
                : p)
            .ToList();

        IReadOnlyList<ResolvedConnection> resolvedConnections = ConnectionResolver.Resolve(
            project.Topology.Connections, project.Services, clusteredServices, clusterPrimaryMap);

        // Inject resolved env vars — group-aware for replicated services
        List<ServiceDefinition> orderedServices = InjectEnvVars(clusteredServices, resolvedConnections);

        // Inject ADAPTER_OP_*_URL — name-based lookup after expansion
        orderedServices = InjectAdapterOpUrls(orderedServices, expandedAdapters);

        // Inject SYNTETIC_CONFIG JSON
        orderedServices = InjectSynteticConfig(orderedServices);

        Dictionary<string, ServiceDefinition> serviceByNameFinal = orderedServices.ToDictionary(s => s.Name);

        List<AdapterDefinition> compiledAdapters = InjectAdapterEnvVars(
            expandedAdapters, serviceByNameFinal, project.Services, clusterPrimaryMap);

        // Build proxy lookups for timeline compilation
        Dictionary<(string from, string to), List<string>> proxyByEdge =
            BuildProxyEdgeMap(proxies, project.Topology.Connections);

        Dictionary<string, string> proxyByReplica = proxies.ToDictionary(
            p => p.ProxiedService, p => p.Name, StringComparer.OrdinalIgnoreCase);

        List<TimelineScenarioDefinition> compiledTimelines = CompileTimelineScenarios(
            project.TimelineScenarios, serviceByNameFinal, proxyByEdge, proxyByReplica);

        ObservabilityDefinition? observability = project.Observability is null
            ? null
            : CompileObservability(project.Observability, orderedServices, compiledAdapters, clusterPrimaryMap);

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
                        adapters.Add(orig with
                        {
                            Name = $"{adapterBaseName}-{i}",
                            PairedReplicaName = replicaName
                        });
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

    // ── Cluster expansion ─────────────────────────────────────────────────────

    private static (List<ServiceDefinition> services, Dictionary<string, string> clusterPrimaryMap)
        ExpandClusters(List<ServiceDefinition> services, IReadOnlyList<AdapterDefinition> originalAdapters)
    {
        // Detect technology per service from adapters
        Dictionary<string, string> technologyByService = originalAdapters
            .Where(a => a.ConnectsTo is not null)
            .GroupBy(a => a.ConnectsTo!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Technology, StringComparer.OrdinalIgnoreCase);

        List<ServiceDefinition> result = [];
        Dictionary<string, string> clusterPrimaryMap = new(StringComparer.OrdinalIgnoreCase);

        foreach (ServiceDefinition service in services)
        {
            if (service.Cluster is null)
            {
                result.Add(service);
                continue;
            }

            ClusterDefinition cluster = service.Cluster;
            string primaryName = $"{service.Name}-primary";
            technologyByService.TryGetValue(service.Name, out string? technology);

            clusterPrimaryMap[service.Name] = primaryName;

            ReadinessConfig? clusterReadiness = BuildClusterReadiness(technology);

            // Primary node — inherits all original env vars + replication master config
            Dictionary<string, string> primaryEnv = BuildPrimaryEnv(service.Env, cluster, technology);
            result.Add(service with
            {
                Name = primaryName,
                Cluster = null,
                Replicas = 1,
                ServiceGroup = service.Name,
                Env = primaryEnv,
                Readiness = service.Readiness ?? clusterReadiness
            });

            // Replica nodes
            for (int i = 0; i < cluster.Replicas; i++)
            {
                string replicaName = $"{service.Name}-replica-{i}";
                Dictionary<string, string> replicaEnv = BuildReplicaEnv(
                    service.Env, cluster, technology, primaryName, service.Port.Value);

                result.Add(service with
                {
                    Name = replicaName,
                    Cluster = null,
                    Replicas = 1,
                    ServiceGroup = service.Name,
                    Env = replicaEnv,
                    Readiness = clusterReadiness
                });
            }
        }

        return (result, clusterPrimaryMap);
    }

    private static ReadinessConfig? BuildClusterReadiness(string? technology)
    {
        string? logPattern = technology?.ToLowerInvariant() switch
        {
            "redis" => "Ready to accept connections",
            // matches both primary ("accept connections") and standby ("accept read-only connections")
            "postgres" => "database system is ready to accept",
            _ => null
        };

        return logPattern is null ? null : new ReadinessConfig
        {
            LogPattern = logPattern,
            Timeout = Domain.ValueObjects.Duration.Parse("90s")
        };
    }

    private static Dictionary<string, string> BuildPrimaryEnv(
        IReadOnlyDictionary<string, string> originalEnv,
        ClusterDefinition cluster,
        string? technology)
    {
        Dictionary<string, string> env = new(originalEnv);

        switch (technology?.ToLowerInvariant())
        {
            case "postgres":
                env["POSTGRESQL_REPLICATION_MODE"] = "master";
                env["POSTGRESQL_REPLICATION_USER"] = cluster.ReplicationUser;
                env["POSTGRESQL_REPLICATION_PASSWORD"] = cluster.ReplicationPassword;
                break;

            case "redis":
                env["REDIS_REPLICATION_MODE"] = "master";
                if (!env.ContainsKey("REDIS_PASSWORD"))
                    env["ALLOW_EMPTY_PASSWORD"] = "yes";
                break;
        }

        return env;
    }

    private static Dictionary<string, string> BuildReplicaEnv(
        IReadOnlyDictionary<string, string> originalEnv,
        ClusterDefinition cluster,
        string? technology,
        string primaryName,
        int port)
    {
        switch (technology?.ToLowerInvariant())
        {
            case "postgres":
            {
                Dictionary<string, string> env = [];
                // Replica needs password for health checks
                if (originalEnv.TryGetValue("POSTGRESQL_PASSWORD", out string? pgPwd)
                    || originalEnv.TryGetValue("POSTGRES_PASSWORD", out pgPwd))
                    env["POSTGRESQL_PASSWORD"] = pgPwd;

                // Replica max_connections must be >= primary's; propagate if set
                if (originalEnv.TryGetValue("POSTGRESQL_MAX_CONNECTIONS", out string? maxConn))
                    env["POSTGRESQL_MAX_CONNECTIONS"] = maxConn;

                env["POSTGRESQL_REPLICATION_MODE"] = "slave";
                env["POSTGRESQL_MASTER_HOST"] = primaryName;
                env["POSTGRESQL_MASTER_PORT_NUMBER"] = port.ToString();
                env["POSTGRESQL_REPLICATION_USER"] = cluster.ReplicationUser;
                env["POSTGRESQL_REPLICATION_PASSWORD"] = cluster.ReplicationPassword;
                return env;
            }

            case "redis":
            {
                Dictionary<string, string> env = [];
                if (originalEnv.TryGetValue("REDIS_PASSWORD", out string? redisPwd))
                {
                    env["REDIS_PASSWORD"] = redisPwd;
                    env["REDIS_MASTER_PASSWORD"] = redisPwd;
                }
                else
                {
                    env["ALLOW_EMPTY_PASSWORD"] = "yes";
                }

                env["REDIS_REPLICATION_MODE"] = "slave";
                env["REDIS_MASTER_HOST"] = primaryName;
                env["REDIS_MASTER_PORT_NUMBER"] = port.ToString();
                return env;
            }

            default:
                return new Dictionary<string, string>(originalEnv);
        }
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
        IReadOnlyList<ServiceDefinition> originalServices,
        Dictionary<string, string> clusterPrimaryMap)
    {
        // Use original services for looking up infra service config when cluster nodes not in services dict
        Dictionary<string, ServiceDefinition> originalByName = originalServices.ToDictionary(s => s.Name);

        return adapters.Select(adapter =>
        {
            if (adapter.ConnectsTo is null) return adapter;

            // Resolve cluster group name → primary node name
            string resolvedTarget = clusterPrimaryMap.TryGetValue(adapter.ConnectsTo, out string? primaryName)
                ? primaryName
                : adapter.ConnectsTo;

            // Prefer cluster-expanded node in services; fallback to original
            Dictionary<string, ServiceDefinition> lookup = services.ContainsKey(resolvedTarget)
                ? services
                : originalByName;

            if (adapter.Technology == "postgres")
            {
                string cs = BuildPgConnectionString(resolvedTarget, lookup);
                Dictionary<string, string> env = new(adapter.Env) { ["PG_CONNECTION_STRING"] = cs };
                return adapter with { Env = env };
            }

            if (adapter.Technology == "redis")
            {
                string cs = BuildRedisConnectionString(resolvedTarget, lookup);
                Dictionary<string, string> env = new(adapter.Env) { ["REDIS_CONNECTION_STRING"] = cs };
                return adapter with { Env = env };
            }

            if (adapter.Technology == "http")
            {
                string url = BuildHttpTargetUrl(resolvedTarget, lookup);
                Dictionary<string, string> env = new(adapter.Env) { ["HTTP_TARGET_URL"] = url };
                return adapter with { Env = env };
            }

            if (adapter.Technology == "kafka")
            {
                ServiceDefinition kafkaService = lookup[resolvedTarget];
                string brokers = $"{resolvedTarget}:{kafkaService.Port.Value}";
                string topic = adapter.Name.Contains('-')
                    ? adapter.Name[..adapter.Name.LastIndexOf('-')]  // strip replica index → base name
                    : adapter.Name;

                Dictionary<string, string> env = new(adapter.Env)
                {
                    ["KAFKA_BROKERS"] = brokers,
                    ["KAFKA_TOPIC"]   = topic
                };

                if (adapter.KafkaConsumer is not null && adapter.PairedReplicaName is not null)
                {
                    ServiceDefinition pairedService = services[adapter.PairedReplicaName];
                    string targetUrl = $"http://{adapter.PairedReplicaName}:{pairedService.Port.Value}";

                    env["KAFKA_CONSUMER_GROUP_ID"]     = adapter.KafkaConsumer.GroupId;
                    env["KAFKA_CONSUMER_TARGET_URL"]   = targetUrl;
                    env["KAFKA_CONSUMER_ENDPOINT"]     = $"/{adapter.KafkaConsumer.Endpoint}";
                    env["KAFKA_CONSUMER_COUNT"]        = adapter.KafkaConsumer.ConsumerCount.ToString();
                    env["KAFKA_PARTITION_COUNT"]       = adapter.KafkaConsumer.PartitionCount.ToString();
                }

                return adapter with { Env = env };
            }

            if (adapter.Technology == "rabbitmq")
            {
                ServiceDefinition rmqService = lookup[resolvedTarget];
                string amqpUrl = BuildRabbitMqUrl(resolvedTarget, rmqService);
                string queue = adapter.Name.Contains('-')
                    ? adapter.Name[..adapter.Name.LastIndexOf('-')]
                    : adapter.Name;

                Dictionary<string, string> env = new(adapter.Env)
                {
                    ["RABBITMQ_URL"]   = amqpUrl,
                    ["RABBITMQ_QUEUE"] = queue
                };

                if (adapter.RabbitMqConsumer is not null && adapter.PairedReplicaName is not null)
                {
                    ServiceDefinition pairedService = services[adapter.PairedReplicaName];
                    string targetUrl = $"http://{adapter.PairedReplicaName}:{pairedService.Port.Value}";

                    env["RABBITMQ_CONSUMER_TARGET_URL"] = targetUrl;
                    env["RABBITMQ_CONSUMER_ENDPOINT"]   = $"/{adapter.RabbitMqConsumer.Endpoint}";
                    env["RABBITMQ_CONSUMER_COUNT"]      = adapter.RabbitMqConsumer.ConsumerCount.ToString();
                    env["RABBITMQ_DURABLE"]             = adapter.RabbitMqConsumer.Durable.ToString().ToLowerInvariant();
                    env["RABBITMQ_PREFETCH"]            = adapter.RabbitMqConsumer.Prefetch.ToString();
                }

                return adapter with { Env = env };
            }

            return adapter;
        }).ToList();
    }

    private static string BuildRabbitMqUrl(string resolvedName, ServiceDefinition service)
    {
        service.Env.TryGetValue("RABBITMQ_DEFAULT_USER", out string? user);
        service.Env.TryGetValue("RABBITMQ_DEFAULT_PASS", out string? pass);
        user ??= "guest";
        pass ??= "guest";
        return $"amqp://{user}:{pass}@{resolvedName}:{service.Port.Value}/";
    }

    private static string BuildPgConnectionString(string resolvedName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(resolvedName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{resolvedName}' not found.");

        string host = service.Proxy ?? resolvedName;

        // Support both Bitnami (POSTGRESQL_*) and official (POSTGRES_*) env var naming
        service.Env.TryGetValue("POSTGRESQL_DATABASE", out string? db);
        if (db is null) service.Env.TryGetValue("POSTGRES_DB", out db);

        service.Env.TryGetValue("POSTGRESQL_USERNAME", out string? user);
        if (user is null) service.Env.TryGetValue("POSTGRES_USER", out user);

        service.Env.TryGetValue("POSTGRESQL_PASSWORD", out string? password);
        if (password is null) service.Env.TryGetValue("POSTGRES_PASSWORD", out password);

        return $"Host={host};Port=5432;Database={db};Username={user};Password={password}";
    }

    private static string BuildRedisConnectionString(string resolvedName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(resolvedName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{resolvedName}' not found.");

        string host = service.Proxy ?? resolvedName;
        service.Env.TryGetValue("REDIS_PASSWORD", out string? password);

        return string.IsNullOrEmpty(password) ? $"{host}:6379" : $"{host}:6379,password={password}";
    }

    private static string BuildHttpTargetUrl(string resolvedName, Dictionary<string, ServiceDefinition> services)
    {
        if (!services.TryGetValue(resolvedName, out ServiceDefinition? service))
            throw new InvalidOperationException($"Adapter connects-to service '{resolvedName}' not found.");

        // Replicated services: Docker group alias = service name (not proxy base name like "backend-proxy").
        // Single/non-replicated: prefer the proxy name (e.g. "redis-proxy"), fall back to service name.
        string host = service.Replicas > 1 ? resolvedName : (service.Proxy ?? resolvedName);
        return $"http://{host}:{service.Port.Value}";
    }

    // ── Observability compilation ─────────────────────────────────────────────

    private static ObservabilityDefinition CompileObservability(
        ObservabilityDefinition observability,
        IReadOnlyList<ServiceDefinition> services,
        IReadOnlyList<AdapterDefinition> adapters,
        Dictionary<string, string> clusterPrimaryMap)
    {
        WarnIfUnsupportedVersion(observability.Prometheus.Image, "Prometheus", new Version(2, 40, 0));
        WarnIfUnsupportedVersion(observability.Grafana.Image, "Grafana", new Version(10, 0, 0));

        // Deduplicate exporters by ConnectsTo service (avoid one exporter per replica-adapter)
        HashSet<string> seenServices = new(StringComparer.OrdinalIgnoreCase);
        List<ExporterDefinition> exporters = [];

        Dictionary<string, ServiceDefinition> serviceByName = services.ToDictionary(s => s.Name);

        foreach (AdapterDefinition adapter in adapters)
        {
            if (adapter.ConnectsTo is null || !seenServices.Add(adapter.ConnectsTo)) continue;

            // Resolve cluster group name → primary node to find the service definition
            string resolvedTarget = clusterPrimaryMap.TryGetValue(adapter.ConnectsTo, out string? primaryName)
                ? primaryName
                : adapter.ConnectsTo;

            if (!serviceByName.TryGetValue(resolvedTarget, out ServiceDefinition? service)) continue;

            ExporterDefinition? exporter = adapter.Technology.ToLowerInvariant() switch
            {
                "redis" => BuildRedisExporter(service),
                "postgres" => BuildPostgresExporter(service),
                "kafka" => BuildKafkaExporter(service),
                "rabbitmq" => BuildRabbitMqExporter(service),
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
        // Support both Bitnami (POSTGRESQL_*) and official (POSTGRES_*) env var naming
        service.Env.TryGetValue("POSTGRESQL_DATABASE", out string? db);
        if (db is null) service.Env.TryGetValue("POSTGRES_DB", out db);

        service.Env.TryGetValue("POSTGRESQL_USERNAME", out string? user);
        if (user is null) service.Env.TryGetValue("POSTGRES_USER", out user);

        service.Env.TryGetValue("POSTGRESQL_PASSWORD", out string? password);
        if (password is null) service.Env.TryGetValue("POSTGRES_PASSWORD", out password);

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

    private static ExporterDefinition BuildKafkaExporter(ServiceDefinition service) =>
        new()
        {
            Name = $"{service.Name}-exporter",
            Image = "danielqsj/kafka-exporter:v1.8.0",
            ServiceName = service.Name,
            Technology = "kafka",
            ExporterPort = 9308,
            Args = [$"--kafka.server={service.Name}:{service.Port.Value}"]
        };

    private static ExporterDefinition BuildRabbitMqExporter(ServiceDefinition service)
    {
        service.Env.TryGetValue("RABBITMQ_DEFAULT_USER", out string? user);
        service.Env.TryGetValue("RABBITMQ_DEFAULT_PASS", out string? pass);
        user ??= "guest";
        pass ??= "guest";

        return new()
        {
            Name = $"{service.Name}-exporter",
            Image = "kbudde/rabbitmq-exporter:v1.0.0-RC19",
            ServiceName = service.Name,
            Technology = "rabbitmq",
            ExporterPort = 9419,
            Env = new Dictionary<string, string>
            {
                ["RABBIT_URL"]      = $"http://{service.Name}:15672",
                ["RABBIT_USER"]     = user,
                ["RABBIT_PASSWORD"] = pass,
                ["RABBIT_EXPORTERS"] = "connections,exchange,node,queue"
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
