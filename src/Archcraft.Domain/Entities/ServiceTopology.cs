namespace Archcraft.Domain.Entities;

/// <summary>
/// Graph of connections between services. Separate from the services list.
/// Responsible for dependency ordering and connection lookup.
/// </summary>
public sealed class ServiceTopology
{
    private readonly IReadOnlyList<ConnectionDefinition> _connections;

    public ServiceTopology(IReadOnlyList<ConnectionDefinition> connections)
    {
        _connections = connections;
    }

    public IReadOnlyList<ConnectionDefinition> Connections => _connections;

    /// <summary>Returns service names in startup order (dependencies first — leafs first).</summary>
    public IReadOnlyList<string> GetStartupOrder(IReadOnlyList<ServiceDefinition> services)
    {
        var names = services.Select(s => s.Name).ToHashSet();
        var inDegree = names.ToDictionary(n => n, _ => 0);
        var adjacency = names.ToDictionary(n => n, _ => new List<string>());

        foreach (ConnectionDefinition connection in _connections)
        {
            // from depends on to → to must start first
            adjacency[connection.To].Add(connection.From);
            inDegree[connection.From]++;
        }

        // Kahn's algorithm
        Queue<string> queue = new(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        List<string> order = [];

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            order.Add(current);

            foreach (string dependent in adjacency[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (order.Count != names.Count)
            throw new InvalidOperationException("Circular dependency detected in service connections.");

        return order;
    }

    public IReadOnlyList<ConnectionDefinition> GetConnectionsFrom(string serviceName) =>
        _connections.Where(c => c.From == serviceName).ToList();
}
