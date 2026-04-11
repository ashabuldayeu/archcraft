using System.Net.Http.Json;
using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Archcraft.Execution;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.Logging;

namespace Archcraft.Execution.Docker;

public sealed class DockerEnvironmentRunner : IEnvironmentRunner
{
    private readonly ILogger<DockerEnvironmentRunner> _logger;
    private readonly List<IContainer> _containers = [];
    private readonly Dictionary<string, IContainer> _containerByServiceName = new();
    private readonly EnvironmentContext _context = new();
    private INetwork? _network;

    public DockerEnvironmentRunner(ILogger<DockerEnvironmentRunner> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(ExecutionPlan plan, CancellationToken cancellationToken = default)
    {
        TestcontainersSettings.WaitStrategyTimeout = TimeSpan.FromMinutes(2);

        _logger.LogInformation("Creating Docker network '{NetworkName}'...", plan.NetworkName);
        _network = new NetworkBuilder()
            .WithName(plan.NetworkName)
            .Build();
        await _network.CreateAsync(cancellationToken);

        foreach (ServiceDefinition service in plan.OrderedServices)
        {
            _logger.LogInformation("Starting service '{ServiceName}' ({Image})...", service.Name, service.Image);
            IContainer container = await StartServiceAsync(service, plan.NetworkName, cancellationToken);
            _containers.Add(container);

            string host = container.Hostname;
            int mappedPort = container.GetMappedPublicPort(service.Port.Value);

            RunningService runningService = new()
            {
                Name = service.Name,
                Host = host,
                MappedPort = mappedPort
            };
            _context.Register(runningService);
            _containerByServiceName[service.Name] = container;

            if (service.ServiceGroup is not null)
                _context.RegisterGroup(service.ServiceGroup, service.Name);

            _logger.LogInformation("Service '{ServiceName}' ready at {Host}:{Port}.", service.Name, host, mappedPort);
        }

        foreach (ProxyDefinition proxy in plan.Proxies)
        {
            _logger.LogInformation("Starting proxy '{ProxyName}' → {ProxiedService}:{Port}...",
                proxy.Name, proxy.ProxiedService, proxy.Port);

            (IContainer container, int mappedApiPort) = await StartProxyAsync(proxy, plan.NetworkName, cancellationToken);
            _containers.Add(container);

            string apiUrl = $"http://localhost:{mappedApiPort}";
            await ConfigureProxyAsync(proxy, apiUrl, cancellationToken);

            _context.RegisterProxy(new RunningProxy
            {
                Name = proxy.Name,
                ProxiedService = proxy.ProxiedService,
                ApiUrl = apiUrl,
                ListenPort = proxy.Port
            });

            _logger.LogInformation("Proxy '{ProxyName}' ready. API: {ApiUrl}", proxy.Name, apiUrl);
        }

        foreach (AdapterDefinition adapter in plan.Adapters)
        {
            _logger.LogInformation("Starting adapter '{AdapterName}' ({Image})...", adapter.Name, adapter.Image);
            IContainer container = await StartAdapterAsync(adapter, plan.NetworkName, cancellationToken);
            _containers.Add(container);

            _logger.LogInformation("Adapter '{AdapterName}' started.", adapter.Name);
        }
    }

    private const string ToxiProxyImage = "ghcr.io/shopify/toxiproxy:2.12.0";
    private const int ToxiProxyApiPort = 8474;

    private async Task<(IContainer container, int mappedApiPort)> StartProxyAsync(
        ProxyDefinition proxy,
        string networkName,
        CancellationToken cancellationToken)
    {
        // Per-replica proxies get both their unique name and the shared group alias for DNS RR
        string[] aliases = proxy.ServiceGroup != proxy.Name
            ? [proxy.Name, proxy.ServiceGroup]
            : [proxy.Name];

        IContainer container = new ContainerBuilder(ToxiProxyImage)
            .WithNetwork(networkName)
            .WithNetworkAliases(aliases)
            .WithPortBinding(ToxiProxyApiPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(req => req
                    .ForPath("/version")
                    .ForPort(ToxiProxyApiPort)))
            .Build();

        await container.StartAsync(cancellationToken);
        int mappedApiPort = container.GetMappedPublicPort(ToxiProxyApiPort);
        return (container, mappedApiPort);
    }

    private static async Task ConfigureProxyAsync(
        ProxyDefinition proxy,
        string apiUrl,
        CancellationToken cancellationToken)
    {
        using HttpClient client = new();

        object body = new
        {
            name = proxy.Name,
            listen = $"0.0.0.0:{proxy.Port}",
            upstream = $"{proxy.UpstreamHost}:{proxy.Port}",
            enabled = true
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{apiUrl}/proxies", body, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private async Task<IContainer> StartAdapterAsync(
        AdapterDefinition adapter,
        string networkName,
        CancellationToken cancellationToken)
    {
        ContainerBuilder builder = new ContainerBuilder(adapter.Image)
            .WithNetwork(networkName)
            .WithNetworkAliases(adapter.Name)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Application started"));

        foreach ((string key, string value) in adapter.Env)
            builder = builder.WithEnvironment(key, value);

        IContainer container = builder.Build();
        await container.StartAsync(cancellationToken);
        return container;
    }

    private async Task<IContainer> StartServiceAsync(
        ServiceDefinition service,
        string networkName,
        CancellationToken cancellationToken)
    {
        ContainerBuilder builder = new ContainerBuilder(service.Image)
            .WithNetwork(networkName)
            .WithNetworkAliases(service.Name)
            .WithPortBinding(service.Port.Value, true);

        foreach ((string key, string value) in service.Env)
            builder = builder.WithEnvironment(key, value);

        if (service.Readiness?.Path is not null)
        {
            builder = builder.WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req
                        .ForPath(service.Readiness.Path)
                        .ForPort((ushort)service.Port.Value),
                        r => r.WithTimeout(service.Readiness.Timeout.Value)));
        }
        else if (service.Readiness?.LogPattern is not null)
        {
            builder = builder.WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilMessageIsLogged(service.Readiness.LogPattern,
                        r => r.WithTimeout(service.Readiness.Timeout.Value)));
        }

        IContainer container = builder.Build();
        await container.StartAsync(cancellationToken);
        return container;
    }

    public async Task<string?> StartObservabilityAsync(
        ExecutionPlan plan,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        if (plan.Observability is null)
            return null;

        ObservabilityDefinition obs = plan.Observability;
        string dashboardsDir = Path.Combine(projectDirectory, "dashboards");

        foreach (ExporterDefinition exporter in obs.Exporters)
        {
            _logger.LogInformation("Starting exporter '{ExporterName}' ({Image})...", exporter.Name, exporter.Image);
            IContainer container = await StartExporterAsync(exporter, plan.NetworkName, cancellationToken);
            _containers.Add(container);
            _logger.LogInformation("Exporter '{ExporterName}' started.", exporter.Name);
        }

        _logger.LogInformation("Starting Prometheus ({Image})...", obs.Prometheus.Image);
        IContainer prometheusContainer = await StartPrometheusAsync(
            obs.Prometheus, dashboardsDir, plan.NetworkName, cancellationToken);
        _containers.Add(prometheusContainer);
        int promPort = prometheusContainer.GetMappedPublicPort(9090);
        _logger.LogInformation("Prometheus ready at http://localhost:{Port}", promPort);

        _logger.LogInformation("Starting Grafana ({Image})...", obs.Grafana.Image);
        IContainer grafanaContainer = await StartGrafanaAsync(
            obs.Grafana, dashboardsDir, plan.NetworkName, cancellationToken);
        _containers.Add(grafanaContainer);
        int grafanaPort = grafanaContainer.GetMappedPublicPort(obs.Grafana.Port);

        string grafanaUrl = $"http://localhost:{grafanaPort}";
        Console.WriteLine();
        Console.WriteLine($"  Grafana:  {grafanaUrl}  (admin / admin)");
        Console.WriteLine();

        return grafanaUrl;
    }

    private static async Task<IContainer> StartExporterAsync(
        ExporterDefinition exporter,
        string networkName,
        CancellationToken cancellationToken)
    {
        ContainerBuilder builder = new ContainerBuilder(exporter.Image)
            .WithNetwork(networkName)
            .WithNetworkAliases(exporter.Name);

        foreach ((string key, string value) in exporter.Env)
            builder = builder.WithEnvironment(key, value);

        IContainer container = builder.Build();
        await container.StartAsync(cancellationToken);
        return container;
    }

    private static async Task<IContainer> StartPrometheusAsync(
        PrometheusConfig prometheus,
        string dashboardsDir,
        string networkName,
        CancellationToken cancellationToken)
    {
        string configPath = Path.Combine(dashboardsDir, "prometheus.yml");

        IContainer container = new ContainerBuilder(prometheus.Image)
            .WithNetwork(networkName)
            .WithNetworkAliases("prometheus")
            .WithPortBinding(9090, true)
            .WithResourceMapping(configPath, "/etc/prometheus/")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(req => req
                    .ForPath("/api/v1/status/runtimeinfo")
                    .ForPort(9090)))
            .Build();

        await container.StartAsync(cancellationToken);
        return container;
    }

    private static async Task<IContainer> StartGrafanaAsync(
        GrafanaConfig grafana,
        string dashboardsDir,
        string networkName,
        CancellationToken cancellationToken)
    {
        string datasourcePath = Path.Combine(dashboardsDir, "provisioning", "datasources", "datasource.yml");
        string dashboardProvPath = Path.Combine(dashboardsDir, "provisioning", "dashboards", "dashboards.yml");

        IContainer container = new ContainerBuilder(grafana.Image)
            .WithNetwork(networkName)
            .WithNetworkAliases("grafana")
            .WithPortBinding(grafana.Port, true)
            .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
            .WithEnvironment("GF_AUTH_DISABLE_LOGIN_FORM", "true")
            .WithResourceMapping(datasourcePath, "/etc/grafana/provisioning/datasources/")
            .WithResourceMapping(dashboardProvPath, "/etc/grafana/provisioning/dashboards/")
            .WithBindMount(dashboardsDir, "/var/lib/grafana/dashboards")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(req => req
                    .ForPath("/api/health")
                    .ForPort((ushort)grafana.Port)))
            .Build();

        await container.StartAsync(cancellationToken);
        return container;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping {Count} container(s)...", _containers.Count);
        foreach (IContainer container in _containers)
        {
            try { await container.StopAsync(cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to stop container gracefully."); }
        }
    }

    public string GetMappedAddress(string serviceName)
    {
        RunningService service = _context.Get(serviceName);
        return service.Address;
    }

    public IReadOnlyList<string> GetGroupAddresses(string groupName) =>
        _context.GetGroup(groupName).Select(s => s.Address).ToList();

    public async Task KillReplicaAsync(string replicaName, CancellationToken cancellationToken = default)
    {
        if (!_containerByServiceName.TryGetValue(replicaName, out IContainer? container))
            throw new InvalidOperationException($"No container found for replica '{replicaName}'.");

        _logger.LogInformation("Killing replica '{ReplicaName}'...", replicaName);
        await container.StopAsync(cancellationToken);
        _logger.LogInformation("Replica '{ReplicaName}' killed.", replicaName);
    }

    public async Task RestoreReplicaAsync(string replicaName, CancellationToken cancellationToken = default)
    {
        if (!_containerByServiceName.TryGetValue(replicaName, out IContainer? container))
            throw new InvalidOperationException($"No container found for replica '{replicaName}'.");

        _logger.LogInformation("Restoring replica '{ReplicaName}'...", replicaName);
        await container.StartAsync(cancellationToken);
        _logger.LogInformation("Replica '{ReplicaName}' restored.", replicaName);
    }

    public async Task RestartContainerAsync(string alias, CancellationToken cancellationToken = default)
    {
        if (!_containerByServiceName.TryGetValue(alias, out IContainer? container))
            throw new InvalidOperationException($"No container found for alias '{alias}'.");

        _logger.LogInformation("Restarting container '{Alias}'...", alias);
        await container.StopAsync(cancellationToken);
        await container.StartAsync(cancellationToken);
        _logger.LogInformation("Container '{Alias}' restarted.", alias);
    }

    public string GetProxyApiUrl(string proxyName)
    {
        RunningProxy proxy = _context.AllProxies
            .FirstOrDefault(p => p.Name == proxyName)
            ?? throw new InvalidOperationException($"Proxy '{proxyName}' is not running.");
        return proxy.ApiUrl;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IContainer container in _containers)
        {
            try { await container.DisposeAsync(); }
            catch { /* best effort */ }
        }

        if (_network is not null)
        {
            try { await _network.DisposeAsync(); }
            catch { /* best effort */ }
        }
    }
}
