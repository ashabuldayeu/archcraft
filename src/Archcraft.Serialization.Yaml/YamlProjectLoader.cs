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
        List<ScenarioDefinition> scenarios = model.Scenarios.Select(MapScenario).ToList();

        return new ProjectDefinition
        {
            Name = model.Name,
            Adapters = adapters,
            Services = services,
            Topology = new ServiceTopology(connections),
            Scenarios = scenarios
        };
    }

    private static AdapterDefinition MapAdapter(AdapterModel model) =>
        new()
        {
            Name = model.Name,
            Image = model.Image,
            Port = new ServicePort(model.Port),
            Technology = model.Technology,
            ConnectsTo = model.ConnectsTo
        };

    private static ServiceDefinition MapService(ServiceModel model) =>
        new()
        {
            Name = model.Name,
            Image = model.Image,
            Port = new ServicePort(model.Port),
            Env = model.Env ?? new Dictionary<string, string>(),
            Readiness = model.Readiness is null ? null : new ReadinessConfig
            {
                Path = model.Readiness.Path,
                Timeout = Duration.Parse(model.Readiness.Timeout)
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
            StartupTimeout = Duration.Parse(model.StartupTimeout)
        };

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
