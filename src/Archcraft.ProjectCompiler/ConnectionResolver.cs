using Archcraft.Domain.Entities;
using Archcraft.Domain.Enums;

namespace Archcraft.ProjectCompiler;

internal static class ConnectionResolver
{
    internal static IReadOnlyList<ResolvedConnection> Resolve(
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyList<ServiceDefinition> originalServices,
        IReadOnlyList<ServiceDefinition> allServices,
        Dictionary<string, string> clusterPrimaryMap)
    {
        // allServices includes cluster-expanded nodes; use for port lookup
        Dictionary<string, int> portByName = allServices.ToDictionary(s => s.Name, s => s.Port.Value);

        // Fallback port lookup for original service names that were replaced by cluster nodes
        Dictionary<string, int> portByOriginal = originalServices.ToDictionary(s => s.Name, s => s.Port.Value);

        List<ResolvedConnection> resolved = [];

        foreach (ConnectionDefinition connection in connections)
        {
            // Resolve actual host: "postgres" → "postgres-primary" when clustered
            string resolvedTo = clusterPrimaryMap.TryGetValue(connection.To, out string? primary)
                ? primary
                : connection.To;

            int port = connection.Port > 0
                ? connection.Port
                : portByName.TryGetValue(resolvedTo, out int p1) ? p1
                : portByOriginal.TryGetValue(connection.To, out int p2) ? p2
                : throw new InvalidOperationException(
                    $"Cannot resolve port for service '{connection.To}'.");

            string envVarName = connection.Alias ?? BuildEnvVarName(connection.To);
            string envVarValue = BuildEnvVarValue(connection.Protocol, resolvedTo, port);

            resolved.Add(new ResolvedConnection
            {
                FromService = connection.From,
                ToService = connection.To,
                EnvVarName = envVarName,
                EnvVarValue = envVarValue
            });
        }

        return resolved;
    }

    private static string BuildEnvVarName(string serviceName) =>
        $"{serviceName.ToUpperInvariant().Replace('-', '_')}_URL";

    private static string BuildEnvVarValue(ConnectionProtocol protocol, string toName, int port) =>
        protocol switch
        {
            ConnectionProtocol.Http => $"http://{toName}:{port}",
            ConnectionProtocol.Grpc => $"grpc://{toName}:{port}",
            ConnectionProtocol.Tcp => $"{toName}:{port}",
            _ => $"{toName}:{port}"
        };
}
