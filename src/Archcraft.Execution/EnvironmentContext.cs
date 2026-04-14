namespace Archcraft.Execution;

public sealed class EnvironmentContext
{
    private readonly Dictionary<string, RunningService> _services = new();
    private readonly Dictionary<string, RunningProxy> _proxies = new();
    private readonly Dictionary<string, List<string>> _serviceGroups = new();
    private readonly Dictionary<string, RunningAdapter> _adapters = new();

    public void Register(RunningService service) =>
        _services[service.Name] = service;

    public void RegisterGroup(string groupName, string replicaName)
    {
        if (!_serviceGroups.TryGetValue(groupName, out List<string>? replicas))
        {
            replicas = [];
            _serviceGroups[groupName] = replicas;
        }
        replicas.Add(replicaName);
    }

    public RunningService Get(string serviceName) =>
        _services.TryGetValue(serviceName, out RunningService? service)
            ? service
            : throw new InvalidOperationException($"Service '{serviceName}' is not running.");

    public bool TryGet(string serviceName, out RunningService? service) =>
        _services.TryGetValue(serviceName, out service);

    public IReadOnlyList<RunningService> GetGroup(string groupName)
    {
        if (_serviceGroups.TryGetValue(groupName, out List<string>? replicaNames))
            return replicaNames.Select(Get).ToList();

        // Single-instance service — treat it as its own group
        if (_services.TryGetValue(groupName, out RunningService? single))
            return [single];

        throw new InvalidOperationException($"No services found for group '{groupName}'.");
    }

    public bool IsGroup(string name) => _serviceGroups.ContainsKey(name);

    public IReadOnlyCollection<RunningService> AllServices => _services.Values;

    public void RegisterProxy(RunningProxy proxy) =>
        _proxies[proxy.Name] = proxy;

    public IReadOnlyCollection<RunningProxy> AllProxies => _proxies.Values;

    public void RegisterAdapter(RunningAdapter adapter) =>
        _adapters[adapter.Name] = adapter;

    public RunningAdapter GetAdapter(string name) =>
        _adapters.TryGetValue(name, out RunningAdapter? adapter)
            ? adapter
            : throw new InvalidOperationException($"Adapter '{name}' is not running.");

    public IReadOnlyCollection<RunningAdapter> AllAdapters => _adapters.Values;
}
