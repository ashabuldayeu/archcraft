using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Archcraft.Domain.Enums;
using Archcraft.Domain.ValueObjects;
using Archcraft.ProjectModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Archcraft.Serialization.Yaml;

public sealed class YamlProjectLoader : IProjectLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public Task<ProjectDefinition> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Project file not found: {filePath}");

        string yaml = File.ReadAllText(filePath);
        ProjectFileModel model = Deserializer.Deserialize<ProjectFileModel>(yaml);

        ProjectDefinition project = MapToProjectDefinition(model);
        return Task.FromResult(project);
    }

    private static ProjectDefinition MapToProjectDefinition(ProjectFileModel model)
    {
        List<AdapterDefinition> adapters = model.Adapters.Select(MapAdapter).ToList();
        List<ServiceDefinition> services = model.Services.Select(MapService).ToList();
        List<ConnectionDefinition> connections = model.Connections.Select(MapConnection).ToList();

        List<ScenarioDefinition> legacyScenarios = model.Scenarios
            .Where(s => s.Timeline is null)
            .Select(MapScenario)
            .ToList();

        List<TimelineScenarioDefinition> timelineScenarios = model.Scenarios
            .Where(s => s.Timeline is not null)
            .Select(MapTimelineScenario)
            .ToList();

        return new ProjectDefinition
        {
            Name = model.Name,
            Adapters = adapters,
            Services = services,
            Topology = new ServiceTopology(connections),
            Scenarios = legacyScenarios,
            TimelineScenarios = timelineScenarios,
            Observability = model.Observability is null ? null : MapObservability(model.Observability)
        };
    }

    private static AdapterDefinition MapAdapter(AdapterModel model) =>
        new()
        {
            Name = model.Name,
            Image = model.Image,
            Port = new ServicePort(model.Port),
            Technology = model.Technology,
            ConnectsTo = model.ConnectsTo,
            Env = model.Env ?? new Dictionary<string, string>()
        };

    private static ServiceDefinition MapService(ServiceModel model) =>
        new()
        {
            Name = model.Name,
            Image = model.Image,
            Port = new ServicePort(model.Port),
            Env = model.Env ?? new Dictionary<string, string>(),
            Proxy = model.Proxy,
            Readiness = model.Readiness is null ? null : new ReadinessConfig
            {
                Path = model.Readiness.Path,
                Timeout = Duration.Parse(model.Readiness.Timeout)
            },
            Replicas = model.Replicas < 1 ? 1 : model.Replicas,
            Cluster = model.Cluster is null ? null : new ClusterDefinition
            {
                Replicas = model.Cluster.Replicas < 1 ? 1 : model.Cluster.Replicas,
                ReplicationUser = model.Cluster.ReplicationUser ?? "replicator",
                ReplicationPassword = model.Cluster.ReplicationPassword ?? "replicator_password"
            },
            SyntheticAdapters = model.Synthetic?.Adapters ?? [],
            SyntheticOperations = model.Synthetic is null
                ? []
                : ExtractOperations(model.Synthetic.Endpoints).Distinct().ToList(),
            SyntheticEndpoints = model.Synthetic is null
                ? []
                : model.Synthetic.Endpoints.Select(MapEndpoint).ToList()
        };

    private static SyntheticEndpoint MapEndpoint(SyntheticEndpointModel model) =>
        new()
        {
            Alias = model.Alias,
            Pipeline = model.Pipeline.Select(MapPipelineStep).ToList()
        };

    private static PipelineStep MapPipelineStep(SyntheticPipelineStepModel model) =>
        new()
        {
            Operation = model.Operation,
            NotFoundRate = model.NotFoundRate,
            Fallback = model.Fallback.Select(MapPipelineStep).ToList(),
            Children = model.Children.Select(MapPipelineStep).ToList()
        };

    private static IEnumerable<string> ExtractOperations(IEnumerable<SyntheticEndpointModel> endpoints)
    {
        foreach (SyntheticEndpointModel endpoint in endpoints)
            foreach (string op in ExtractOperationsFromSteps(endpoint.Pipeline))
                yield return op;
    }

    private static IEnumerable<string> ExtractOperationsFromSteps(IEnumerable<SyntheticPipelineStepModel> steps)
    {
        foreach (SyntheticPipelineStepModel step in steps)
        {
            yield return step.Operation;
            foreach (string op in ExtractOperationsFromSteps(step.Fallback)) yield return op;
            foreach (string op in ExtractOperationsFromSteps(step.Children)) yield return op;
        }
    }

    private static ObservabilityDefinition MapObservability(ObservabilityModel model) =>
        new()
        {
            Prometheus = new PrometheusConfig
            {
                Port = model.Prometheus?.Port ?? 9090,
                Image = model.Prometheus?.Image ?? "prom/prometheus:v3.2.1"
            },
            Grafana = new GrafanaConfig
            {
                Port = model.Grafana?.Port ?? 3000,
                Image = model.Grafana?.Image ?? "grafana/grafana:11.5.2"
            }
        };

    private static ConnectionDefinition MapConnection(ConnectionModel model) =>
        new()
        {
            From = model.From,
            To = model.To,
            Protocol = ParseProtocol(model.Protocol),
            Port = model.Port,
            Alias = model.Alias,
            Via = model.Via
        };

    private static ScenarioDefinition MapScenario(ScenarioModel model) =>
        new()
        {
            Name = model.Name,
            Type = ParseScenarioType(model.Type),
            Target = model.Target,
            Rps = new RpsTarget(model.Rps),
            ScenarioDuration = Duration.Parse(model.Duration),
            StartupTimeout = Duration.Parse(model.StartupTimeout),
            RequestTimeout = model.RequestTimeout is null ? Duration.Parse("5s") : Duration.Parse(model.RequestTimeout),
            DrainTimeout = model.DrainTimeout is null ? null : Duration.Parse(model.DrainTimeout),
            RestartAfter = model.RestartAfter ?? []
        };

    private static TimelineScenarioDefinition MapTimelineScenario(ScenarioModel model) =>
        new()
        {
            Name = model.Name,
            StartupTimeout = Duration.Parse(model.StartupTimeout),
            Timeline = model.Timeline!.Select(MapTimelinePoint).ToList()
        };

    private static TimelinePoint MapTimelinePoint(TimelinePointModel model) =>
        new()
        {
            At = ParseTimeSpan(model.At),
            Actions = model.Actions.Select(MapTimelineAction).ToList()
        };

    private static TimeSpan ParseTimeSpan(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Time string cannot be empty.");

        TimeSpan total = TimeSpan.Zero;
        int i = 0;

        while (i < input.Length)
        {
            int start = i;
            while (i < input.Length && char.IsDigit(input[i])) i++;
            if (i == start)
                throw new FormatException($"Expected digit at position {i} in time '{input}'.");

            int number = int.Parse(input[start..i]);
            if (i >= input.Length)
                throw new FormatException($"Expected unit (s, m, h) after number in time '{input}'.");

            char unit = input[i++];
            total += unit switch
            {
                's' => TimeSpan.FromSeconds(number),
                'm' => TimeSpan.FromMinutes(number),
                'h' => TimeSpan.FromHours(number),
                _ => throw new FormatException($"Unknown time unit '{unit}'.")
            };
        }

        return total;
    }

    private static TimelineAction MapTimelineAction(TimelineActionModel model)
    {
        Duration? duration = model.Duration is not null ? Duration.Parse(model.Duration) : null;

        return model.Type.ToLowerInvariant() switch
        {
            "load" => new LoadAction
            {
                Duration = duration,
                Target = model.Target?.ToString() ?? string.Empty,
                Endpoint = model.Endpoint ?? string.Empty,
                Rps = model.Rps,
                RequestTimeout = model.RequestTimeout is null ? Duration.Parse("5s") : Duration.Parse(model.RequestTimeout)
            },
            "inject_latency" => new InjectLatencyAction
            {
                Duration = duration,
                From = ReadTargetField(model.Target, "from"),
                To = ReadTargetField(model.Target, "to"),
                LatencyMs = ParseLatencyMs(model.Latency ?? "0ms")
            },
            "inject_error" => new InjectErrorAction
            {
                Duration = duration,
                From = ReadTargetField(model.Target, "from"),
                To = ReadTargetField(model.Target, "to"),
                ErrorRate = model.ErrorRate
            },
            "kill" => new KillAction
            {
                Duration = duration,
                Target = model.Target?.ToString() ?? string.Empty
            },
            "restore" => new RestoreAction
            {
                Target = model.Target?.ToString() ?? string.Empty
            },
            _ => throw new InvalidOperationException($"Unknown timeline action type '{model.Type}'. Supported: load, inject_latency, inject_error, kill, restore.")
        };
    }

    private static string ReadTargetField(object? target, string field)
    {
        if (target is null)
            throw new InvalidOperationException($"inject actions require a 'target' with 'from' and 'to' fields.");

        if (target is Dictionary<object, object> dict && dict.TryGetValue(field, out object? value))
            return value?.ToString() ?? string.Empty;

        throw new InvalidOperationException($"inject action target is missing the '{field}' field.");
    }

    private static int ParseLatencyMs(string latency)
    {
        if (latency.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            return int.Parse(latency[..^2]);
        if (latency.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return int.Parse(latency[..^1]) * 1000;
        return int.Parse(latency);
    }

    private static ConnectionProtocol ParseProtocol(string value) =>
        value.ToLowerInvariant() switch
        {
            "http" => ConnectionProtocol.Http,
            "grpc" => ConnectionProtocol.Grpc,
            "tcp" => ConnectionProtocol.Tcp,
            _ => throw new InvalidOperationException($"Unknown protocol '{value}'. Supported: http, grpc, tcp.")
        };

    private static ScenarioType ParseScenarioType(string value) =>
        value.ToLowerInvariant() switch
        {
            "http" => ScenarioType.Http,
            _ => throw new InvalidOperationException($"Unknown scenario type '{value}'. Supported: http.")
        };
}
