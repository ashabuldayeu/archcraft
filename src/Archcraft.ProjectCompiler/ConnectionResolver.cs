using Archcraft.Domain.Entities;
using Archcraft.Domain.Enums;

namespace Archcraft.ProjectCompiler;

internal static class ConnectionResolver
{
    internal static IReadOnlyList<ResolvedConnection> Resolve(
        IReadOnlyList<ConnectionDefinition> connections,
        IReadOnlyList<ServiceDefinition> services)
    {
        Dictionary<string, int> portByName = services.ToDictionary(s => s.Name, s => s.Port.Value);
        List<ResolvedConnection> resolved = [];

        foreach (ConnectionDefinition connection in connections)
        {
            int port = connection.Port > 0 ? connection.Port : portByName[connection.To];
            string envVarName = connection.Alias ?? BuildEnvVarName(connection.To);
            string envVarValue = BuildEnvVarValue(connection.Protocol, connection.To, port);

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
