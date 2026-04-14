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

    private string? _grafanaUrl;

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

    // ── Setup / teardown ──────────────────────────────────────────────────────

    public async Task<(ExecutionPlan Plan, string? GrafanaUrl)> SetupAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        ProjectDefinition project = await _loader.LoadAsync(projectFilePath, cancellationToken);
        ExecutionPlan plan = _compiler.Compile(project);

        string projectDirectory = Path.GetDirectoryName(projectFilePath)!;
        await _dashboardGenerator.GenerateAsync(plan, projectDirectory);

        await _environmentRunner.StartAsync(plan, cancellationToken);
        string? grafanaUrl = await _environmentRunner.StartObservabilityAsync(
            plan, projectDirectory, cancellationToken);

        _grafanaUrl = grafanaUrl;
        return (plan, grafanaUrl);
    }

    public Task TeardownAsync() => _environmentRunner.StopAsync(CancellationToken.None);

    // ── Scenario execution ────────────────────────────────────────────────────

    /// <summary>
    /// Runs scenarios by name. Pass null to run all. Throws if any named scenario is not found.
    /// </summary>
    public async Task<IReadOnlyList<MetricSnapshot>> RunScenariosAsync(
        ExecutionPlan plan,
        IReadOnlyList<string>? scenarioNames = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ScenarioDefinition> legacyToRun;
        IReadOnlyList<TimelineScenarioDefinition> timelinesToRun;

        if (scenarioNames is null)
        {
            legacyToRun = plan.Scenarios;
            timelinesToRun = plan.TimelineScenarios;
        }
        else
        {
            // Validate all requested names exist before running anything
            foreach (string name in scenarioNames)
            {
                bool found = plan.Scenarios.Any(s => s.Name == name)
                          || plan.TimelineScenarios.Any(s => s.Name == name);
                if (!found)
                    throw new InvalidOperationException($"Scenario '{name}' not found in project.");
            }

            legacyToRun = plan.Scenarios.Where(s => scenarioNames.Contains(s.Name)).ToList();
            timelinesToRun = plan.TimelineScenarios.Where(s => scenarioNames.Contains(s.Name)).ToList();
        }

        List<MetricSnapshot> snapshots = [];

        foreach (ScenarioDefinition scenario in legacyToRun)
        {
            ScenarioDefinition resolved = ResolveScenarioTarget(scenario, plan, _environmentRunner);
            IScenarioRunner runner = _scenarioRunners.FirstOrDefault(r => r.CanHandle(resolved))
                ?? throw new InvalidOperationException($"No runner found for scenario type '{resolved.Type}'.");
            MetricSnapshot snapshot = await runner.RunAsync(resolved, _metricsCollector, cancellationToken);
            snapshots.Add(snapshot);
        }

        foreach (TimelineScenarioDefinition scenario in timelinesToRun)
        {
            MetricSnapshot snapshot = await _timelineRunner.RunAsync(scenario, plan, _grafanaUrl, cancellationToken);
            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    // ── Legacy all-in-one entry point (used by ScenarioCommand) ──────────────

    public async Task<RunReport> ExecuteAsync(
        string projectFilePath,
        string? scenarioName = null,
        CancellationToken cancellationToken = default)
    {
        (ExecutionPlan plan, string? grafanaUrl) = await SetupAsync(projectFilePath, cancellationToken);

        try
        {
            IReadOnlyList<string>? names = scenarioName is null ? null : [scenarioName];
            IReadOnlyList<MetricSnapshot> snapshots = await RunScenariosAsync(plan, names, cancellationToken);
            RunReport report = _reportBuilder.Build(plan.ProjectName, snapshots);
            return report with { GrafanaUrl = grafanaUrl };
        }
        finally
        {
            await TeardownAsync();
        }
    }

    // ── Adapter access ────────────────────────────────────────────────────────

    public string GetAdapterBaseUrl(string adapterName) =>
        _environmentRunner.GetAdapterBaseUrl(adapterName);

    public IReadOnlyCollection<string> GetAllAdapterNames() =>
        _environmentRunner.GetAllAdapterNames();

    // ── Helpers ───────────────────────────────────────────────────────────────

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
            target = target.Replace($"{service.Name}:{servicePort}", mappedAddress);
        }

        return scenario with { Target = target };
    }
}
