using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Archcraft.Observability;

namespace Archcraft.App.UseCases;

public sealed class InteractiveSessionUseCase
{
    private readonly RunProjectUseCase _runProject;
    private readonly IReportBuilder _reportBuilder;
    private readonly IProjectLoader _projectLoader;
    private readonly IProjectCompiler _projectCompiler;

    private ExecutionPlan? _currentPlan;
    private readonly object _reloadLock = new();

    public InteractiveSessionUseCase(
        RunProjectUseCase runProject,
        IReportBuilder reportBuilder,
        IProjectLoader projectLoader,
        IProjectCompiler projectCompiler)
    {
        _runProject = runProject;
        _reportBuilder = reportBuilder;
        _projectLoader = projectLoader;
        _projectCompiler = projectCompiler;
    }

    public async Task RunAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        (ExecutionPlan plan, string? grafanaUrl) = await _runProject.SetupAsync(projectFilePath, cancellationToken);

        lock (_reloadLock)
            _currentPlan = plan;

        List<(int RunNumber, MetricSnapshot Snapshot)> sessionResults = [];
        int runCounter = 0;

        using CancellationTokenSource watcherCts = new();
        using FileSystemWatcher watcher = StartFileWatcher(projectFilePath, watcherCts.Token);

        try
        {
            PrintWelcome(plan, grafanaUrl);

            bool shouldStop = false;
            while (!cancellationToken.IsCancellationRequested && !shouldStop)
            {
                Console.Write("> ");
                string? input = await ReadLineAsync(cancellationToken);

                if (input is null)
                    break;

                string trimmed = input.Trim();
                if (trimmed.Length == 0)
                    continue;

                string[] parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                string command = parts[0].ToLowerInvariant();
                string? arg = parts.Length > 1 ? parts[1].Trim() : null;

                switch (command)
                {
                    case "run":
                        ExecutionPlan currentPlan;
                        lock (_reloadLock)
                            currentPlan = _currentPlan!;

                        IReadOnlyList<MetricSnapshot> snapshots =
                            await HandleRunAsync(currentPlan, arg, cancellationToken);
                        if (snapshots.Count > 0)
                        {
                            runCounter++;
                            foreach (MetricSnapshot s in snapshots)
                                sessionResults.Add((runCounter, s));
                            RenderRunTable(snapshots);
                        }
                        break;

                    case "report":
                        if (sessionResults.Count == 0)
                        {
                            Console.WriteLine("  No scenarios have been run yet.");
                        }
                        else
                        {
                            lock (_reloadLock)
                                currentPlan = _currentPlan!;

                            RenderSessionTable(sessionResults, currentPlan.ProjectName, grafanaUrl);
                            RunReport report = _reportBuilder.Build(
                                currentPlan.ProjectName,
                                sessionResults.Select(r => r.Snapshot).ToList());
                            report = report with { GrafanaUrl = grafanaUrl };
                            await JsonReportWriter.WriteAsync(report, projectFilePath, CancellationToken.None);
                        }
                        break;

                    case "help":
                        PrintHelp();
                        break;

                    case "stop":
                        Console.WriteLine("  Stopping...");
                        shouldStop = true;
                        break;

                    default:
                        Console.WriteLine("Unknown command. Type 'help' for available commands.");
                        break;
                }
            }
        }
        finally
        {
            watcherCts.Cancel();
            await _runProject.TeardownAsync();
        }
    }

    private FileSystemWatcher StartFileWatcher(string projectFilePath, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!;
        string fileName = Path.GetFileName(projectFilePath);

        FileSystemWatcher watcher = new(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        CancellationTokenSource? debounceCts = null;

        void OnChanged(object sender, FileSystemEventArgs e)
        {
            CancellationTokenSource? previous = Interlocked.Exchange(ref debounceCts, new CancellationTokenSource());
            previous?.Cancel();
            previous?.Dispose();

            CancellationTokenSource current = debounceCts!;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), current.Token);
                    await ReloadScenariosAsync(projectFilePath, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // debounce cancelled by a newer event — do nothing
                }
            }, CancellationToken.None);
        }

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += OnChanged;

        return watcher;
    }

    private async Task ReloadScenariosAsync(string projectFilePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(projectFilePath))
            return;

        try
        {
            ProjectDefinition project = await _projectLoader.LoadAsync(projectFilePath, cancellationToken);
            ExecutionPlan newPlan = _projectCompiler.Compile(project);

            lock (_reloadLock)
            {
                _currentPlan = _currentPlan! with
                {
                    Scenarios = newPlan.Scenarios,
                    TimelineScenarios = newPlan.TimelineScenarios
                };
            }

            int total = newPlan.Scenarios.Count + newPlan.TimelineScenarios.Count;
            Console.WriteLine();
            Console.WriteLine($"[config] Scenarios reloaded — {total} scenario(s) available.");
            Console.WriteLine("[config] Note: changes to services/adapters/connections take effect only after restart.");
            Console.Write("> ");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"[config] Reload failed: {ex.Message}");
            Console.Write("> ");
        }
    }

    private async Task<IReadOnlyList<MetricSnapshot>> HandleRunAsync(
        ExecutionPlan plan,
        string? arg,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string>? names = arg is null or "all" ? null : [arg];
            return await _runProject.RunScenariosAsync(plan, names, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"  Error: {ex.Message}");
            return [];
        }
    }

    private static void PrintWelcome(ExecutionPlan plan, string? grafanaUrl)
    {
        Console.WriteLine();
        Console.WriteLine($"  Project '{plan.ProjectName}' is running. Type 'help' for available commands.");
        if (grafanaUrl is not null)
            Console.WriteLine($"  Grafana:  {grafanaUrl}  (admin / admin)");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("  Commands:");
        Console.WriteLine("    run all          — run all scenarios");
        Console.WriteLine("    run <name>       — run a specific scenario by name");
        Console.WriteLine("    report           — print session summary and save JSON");
        Console.WriteLine("    help             — show this help");
        Console.WriteLine("    stop             — stop all containers and exit");
        Console.WriteLine();
    }

    private static void RenderRunTable(IReadOnlyList<MetricSnapshot> snapshots)
    {
        Console.WriteLine();
        Console.WriteLine($"  {"Scenario",-25} {"p50 (ms)",10} {"p99 (ms)",10} {"Error %",10} {"Requests",10}");
        Console.WriteLine($"  {new string('─', 69)}");
        foreach (MetricSnapshot s in snapshots)
        {
            Console.WriteLine(
                $"  {s.ScenarioName,-25} {s.P50Ms,10:F1} {s.P99Ms,10:F1} " +
                $"{s.ErrorRate * 100,10:F2} {s.TotalRequests,10}");
        }
        Console.WriteLine();
    }

    private static void RenderSessionTable(
        List<(int RunNumber, MetricSnapshot Snapshot)> results,
        string projectName,
        string? grafanaUrl)
    {
        Console.WriteLine();
        Console.WriteLine($"╔══ Session Report: {projectName} ══════════════════════════════════╗");
        Console.WriteLine($"║  {"Run",5}  {"Scenario",-25} {"p50 (ms)",10} {"p99 (ms)",10} {"Error %",10} {"Req",8}  ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        foreach ((int runNumber, MetricSnapshot s) in results)
        {
            Console.WriteLine(
                $"║  {"#" + runNumber,5}  {s.ScenarioName,-25} {s.P50Ms,10:F1} {s.P99Ms,10:F1} " +
                $"{s.ErrorRate * 100,10:F2} {s.TotalRequests,8}  ║");
        }
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        if (grafanaUrl is not null)
            Console.WriteLine($"  Grafana:  {grafanaUrl}  (admin / admin)");
        Console.WriteLine();
    }

    private static async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(Console.ReadLine, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
