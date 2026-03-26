using Archcraft.Domain.Entities;

namespace Archcraft.Contracts;

public interface IEnvironmentRunner : IAsyncDisposable
{
    Task StartAsync(ExecutionPlan plan, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the host-accessible address for a service: "localhost:PORT".</summary>
    string GetMappedAddress(string serviceName);
}
