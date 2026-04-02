using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Archcraft.Observability;
using Archcraft.Scenarios;
using Microsoft.Extensions.Logging;

namespace Archcraft.App.UseCases;

public sealed class RunProjectUseCase
{
    private readonly IProjectLoader _loader;
    private readonly IProjectCompiler _compiler;
    private readonly IEnvironmentRunner _environmentRunner;
    private readonly IEnumerable<IScenarioRunner> _scenarioRunners;
    private readonly IMetricsCollector _metricsCollector;
    private readonly IReportBuilder _reportBuilder;
    private readonly TimelineScenarioRunner _timelineRunner;
    private readonly DashboardGenerator _dashboardGenerator;
    private readonly ILogger<RunProjectUseCase> _logger;

    public RunProjectUseCase(
        IProjectLoader loader,
        IProjectCompiler compiler,
        IEnvironmentRunner environmentRunner,
        IEnumerable<IScenarioRunner> scenarioRunners,
        IMetricsCollector metricsCollector,
        IReportBuilder reportBuilder,
        TimelineScenarioRunner timelineRunner,
        DashboardGenerator dashboardGenerator,
        ILogger<RunProjectUseCase> logger)
    {
        _loader = loader;
        _compiler = compiler;
        _environmentRunner = environmentRunner;
        _scenarioRunners = scenarioRunners;
        _metricsCollector = metricsCollector;
        _reportBuilder = reportBuilder;
        _timelineRunner = timelineRunner;
        _dashboardGenerator = dashboardGenerator;
        _logger = logger;
    }

    public async Task<RunReport> ExecuteAsync(
        string projectFilePath,
        string? scenarioName = null,
        CancellationToken cancellationToken = default)
    {
        ProjectDefinition project = await _loader.LoadAsync(projectFilePath, cancellationToken);
        ExecutionPlan plan = _compiler.Compile(project);

        string projectDirectory = Path.GetDirectoryName(projectFilePath)!;
        await _dashboardGenerator.GenerateAsync(plan, projectDirectory);

        IReadOnlyList<ScenarioDefinition> legacyScenariosToRun = scenarioName is null
            ? plan.Scenarios
            : plan.Scenarios.Where(s => s.Name == scenarioName).ToList();

        IReadOnlyList<TimelineScenarioDefinition> timelineScenariosToRun = scenarioName is null
            ? plan.TimelineScenarios
            : plan.TimelineScenarios.Where(s => s.Name == scenarioName).ToList();

        if (legacyScenariosToRun.Count == 0 && timelineScenariosToRun.Count == 0 && scenarioName is not null)
            throw new InvalidOperationException($"Scenario '{scenarioName}' not found in project.");

        List<MetricSnapshot> snapshots = [];

        try
        {
            await _environmentRunner.StartAsync(plan, cancellationToken);
            await _environmentRunner.StartObservabilityAsync(plan, projectDirectory, cancellationToken);

            foreach (ScenarioDefinition scenario in legacyScenariosToRun)
            {
                ScenarioDefinition resolved = ResolveScenarioTarget(scenario, plan, _environmentRunner);

                IScenarioRunner runner = _scenarioRunners.FirstOrDefault(r => r.CanHandle(resolved))
                    ?? throw new InvalidOperationException($"No runner found for scenario type '{resolved.Type}'.");

                MetricSnapshot snapshot = await runner.RunAsync(resolved, _metricsCollector, cancellationToken);
                snapshots.Add(snapshot);
            }

            foreach (TimelineScenarioDefinition scenario in timelineScenariosToRun)
            {
                MetricSnapshot snapshot = await _timelineRunner.RunAsync(scenario, plan, cancellationToken);
                snapshots.Add(snapshot);
            }
        }
        finally
        {
            await _environmentRunner.StopAsync(CancellationToken.None);
        }

        return _reportBuilder.Build(project.Name, snapshots);
    }

    private static ScenarioDefinition ResolveScenarioTarget(
        ScenarioDefinition scenario,
        ExecutionPlan plan,
        IEnvironmentRunner runner)
    {
        string target = scenario.Target;

        foreach (ServiceDefinition service in plan.OrderedServices)
        {
            string servicePort = service.Port.Value.ToString();
            string mappedAddress = runner.GetMappedAddress(service.Name);

            // Replace "service-name:port" → "host:mappedPort" for access from host machine
            target = target.Replace($"{service.Name}:{servicePort}", mappedAddress);
        }

        return scenario with { Target = target };
    }
}
