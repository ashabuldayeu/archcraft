namespace Archcraft.Execution;

public sealed class EnvironmentContext
{
    private readonly Dictionary<string, RunningService> _services = new();

    public void Register(RunningService service) =>
        _services[service.Name] = service;

    public RunningService Get(string serviceName) =>
        _services.TryGetValue(serviceName, out RunningService? service)
            ? service
            : throw new InvalidOperationException($"Service '{serviceName}' is not running.");

    public bool TryGet(string serviceName, out RunningService? service) =>
        _services.TryGetValue(serviceName, out service);

    public IReadOnlyCollection<RunningService> AllServices => _services.Values;
}
