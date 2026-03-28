using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Archcraft.Execution;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.Logging;

namespace Archcraft.Execution.Docker;

public sealed class DockerEnvironmentRunner : IEnvironmentRunner
{
    private readonly ILogger<DockerEnvironmentRunner> _logger;
    private readonly List<IContainer> _containers = [];
    private readonly EnvironmentContext _context = new();
    private INetwork? _network;

    public DockerEnvironmentRunner(ILogger<DockerEnvironmentRunner> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(ExecutionPlan plan, CancellationToken cancellationToken = default)
    {
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

            _context.Register(new RunningService
            {
                Name = service.Name,
                Host = host,
                MappedPort = mappedPort
            });

            _logger.LogInformation("Service '{ServiceName}' ready at {Host}:{Port}.", service.Name, host, mappedPort);
        }

        foreach (AdapterDefinition adapter in plan.Adapters)
        {
            _logger.LogInformation("Starting adapter '{AdapterName}' ({Image})...", adapter.Name, adapter.Image);
            IContainer container = await StartAdapterAsync(adapter, plan.NetworkName, cancellationToken);
            _containers.Add(container);

            _logger.LogInformation("Adapter '{AdapterName}' started.", adapter.Name);
        }
    }

    private async Task<IContainer> StartAdapterAsync(
        AdapterDefinition adapter,
        string networkName,
        CancellationToken cancellationToken)
    {
        ContainerBuilder builder = new ContainerBuilder(adapter.Image)
            .WithNetwork(networkName)
            .WithNetworkAliases(adapter.Name)
            .WithPortBinding(adapter.Port.Value, true);

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

        if (service.Readiness is not null)
        {
            builder = builder.WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req
                        .ForPath(service.Readiness.Path)
                        .ForPort((ushort)service.Port.Value),
                        r => r.WithTimeout(service.Readiness.Timeout.Value)));
        }

        IContainer container = builder.Build();
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
