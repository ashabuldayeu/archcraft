using Archcraft.Domain.Entities;

namespace Archcraft.Contracts;

public interface IEnvironmentRunner : IAsyncDisposable
{
    Task StartAsync(ExecutionPlan plan, CancellationToken cancellationToken = default);
    /// <summary>Starts observability stack. Returns Grafana URL if Grafana is running, null otherwise.</summary>
    Task<string?> StartObservabilityAsync(ExecutionPlan plan, string projectDirectory, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task KillReplicaAsync(string replicaName, CancellationToken cancellationToken = default);
    Task RestoreReplicaAsync(string replicaName, CancellationToken cancellationToken = default);
    Task RestartContainerAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>Returns the host-accessible address for a service or replica: "localhost:PORT".</summary>
    string GetMappedAddress(string serviceName);

    /// <summary>Returns host-accessible addresses for all replicas in a service group.</summary>
    IReadOnlyList<string> GetGroupAddresses(string groupName);

    /// <summary>Returns the ToxiProxy API URL (host-accessible) for a named proxy.</summary>
    string GetProxyApiUrl(string proxyName);

    /// <summary>Returns the host-accessible base URL for a named adapter (e.g. "http://localhost:PORT").</summary>
    string GetAdapterBaseUrl(string adapterName);

    /// <summary>Returns all running adapters.</summary>
    IReadOnlyCollection<string> GetAllAdapterNames();
}
