using System.Diagnostics;
using System.Net.Http.Json;
using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Archcraft.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Archcraft.Scenarios;

public sealed class TimelineScenarioRunner
{
    private const int ToxiProxyApiPort = 8474;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEnvironmentRunner _environmentRunner;
    private readonly IMetricsCollector _collector;
    private readonly ILogger<TimelineScenarioRunner> _logger;

    // active load tasks per target
    private readonly Dictionary<string, CancellationTokenSource> _loadCts = new();

    public TimelineScenarioRunner(
        IHttpClientFactory httpClientFactory,
        IEnvironmentRunner environmentRunner,
        IMetricsCollector collector,
        ILogger<TimelineScenarioRunner> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environmentRunner = environmentRunner;
        _collector = collector;
        _logger = logger;
    }

    public async Task<MetricSnapshot> RunAsync(
        TimelineScenarioDefinition scenario,
        ExecutionPlan plan,
        CancellationToken cancellationToken = default)
    {
        _collector.Reset();

        await WaitForSyntheticServicesAsync(scenario, plan, cancellationToken);

        _logger.LogInformation("Running timeline scenario '{Name}'", scenario.Name);

        Stopwatch scenarioTimer = Stopwatch.StartNew();
        List<Task> rollbackTasks = [];

        // Sort points by At
        List<TimelinePoint> points = scenario.Timeline.OrderBy(p => p.At).ToList();

        foreach (TimelinePoint point in points)
        {
            TimeSpan delay = point.At - scenarioTimer.Elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            _logger.LogInformation("Timeline at {At}: executing {Count} action(s)",
                point.At, point.Actions.Count);

            foreach (TimelineAction action in point.Actions)
            {
                Task? rollback = await ExecuteActionAsync(action, plan, cancellationToken);
                if (rollback is not null)
                    rollbackTasks.Add(rollback);
            }
        }

        // Wait for all rollback timers
        await Task.WhenAll(rollbackTasks);

        // Stop any remaining load
        foreach (CancellationTokenSource cts in _loadCts.Values)
            cts.Cancel();

        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        (double p50, double p99, double errorRate, int total, IReadOnlyList<double> raw) = _collector.GetStats();

        return new MetricSnapshot
        {
            ScenarioName = scenario.Name,
            P50Ms = p50,
            P99Ms = p99,
            ErrorRate = errorRate,
            TotalRequests = total,
            RawLatenciesMs = raw
        };
    }

    private async Task<Task?> ExecuteActionAsync(
        TimelineAction action,
        ExecutionPlan plan,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case LoadAction load:
                StartLoad(load, plan);
                if (load.Duration.HasValue)
                    return StopLoadAfterAsync(load.Target, load.Duration.Value.Value, cancellationToken);
                return null;

            case InjectLatencyAction latency:
                await InjectLatencyAsync(latency, cancellationToken);
                if (latency.Duration.HasValue)
                    return RemoveToxicAfterAsync(latency.ProxyName!, "latency-inject", latency.Duration.Value.Value, cancellationToken);
                return null;

            case InjectErrorAction error:
                await InjectErrorAsync(error, cancellationToken);
                if (error.Duration.HasValue)
                    return RemoveToxicAfterAsync(error.ProxyName!, "error-inject", error.Duration.Value.Value, cancellationToken);
                return null;

            default:
                return null;
        }
    }

    private void StartLoad(LoadAction load, ExecutionPlan plan)
    {
        // Cancel existing load for same target
        if (_loadCts.TryGetValue(load.Target, out CancellationTokenSource? existing))
        {
            existing.Cancel();
            _loadCts.Remove(load.Target);
        }

        string mappedAddress = _environmentRunner.GetMappedAddress(load.Target);
        string url = $"http://{mappedAddress}/{load.Endpoint}";

        CancellationTokenSource cts = new();
        _loadCts[load.Target] = cts;

        _ = RunLoadLoopAsync(url, load.Rps, cts.Token);

        _logger.LogInformation("Load started: {Rps} RPS → {Url}", load.Rps, url);
    }

    private async Task RunLoadLoopAsync(string url, int rps, CancellationToken cancellationToken)
    {
        TimeSpan interval = TimeSpan.FromMilliseconds(1000.0 / rps);

        while (!cancellationToken.IsCancellationRequested)
        {
            Stopwatch requestTimer = Stopwatch.StartNew();
            _ = FireRequestAsync(url, cancellationToken);

            TimeSpan wait = interval - requestTimer.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task FireRequestAsync(string url, CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        Stopwatch timer = Stopwatch.StartNew();
        bool success = false;

        try
        {
            HttpResponseMessage response = await client.PostAsync(url, content: null, cancellationToken);
            timer.Stop();
            success = response.IsSuccessStatusCode;
        }
        catch
        {
            timer.Stop();
            success = false;
        }

        _collector.RecordRequest(timer.Elapsed, success);
    }

    private async Task StopLoadAfterAsync(string target, TimeSpan duration, CancellationToken cancellationToken)
    {
        await Task.Delay(duration, cancellationToken);
        if (_loadCts.TryGetValue(target, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            _loadCts.Remove(target);
            _logger.LogInformation("Load stopped for target '{Target}' after {Duration}", target, duration);
        }
    }

    private async Task InjectLatencyAsync(InjectLatencyAction action, CancellationToken cancellationToken)
    {
        string apiUrl = _environmentRunner.GetProxyApiUrl(action.ProxyName!);

        object toxic = new
        {
            name = "latency-inject",
            type = "latency",
            stream = "downstream",
            toxicity = 1.0,
            attributes = new { latency = action.LatencyMs, jitter = 0 }
        };

        await PostToxicAsync(apiUrl, action.ProxyName!, toxic, cancellationToken);
        _logger.LogInformation("Injected latency {LatencyMs}ms on proxy '{Proxy}'", action.LatencyMs, action.ProxyName);
    }

    private async Task InjectErrorAsync(InjectErrorAction action, CancellationToken cancellationToken)
    {
        string apiUrl = _environmentRunner.GetProxyApiUrl(action.ProxyName!);

        object toxic = new
        {
            name = "error-inject",
            type = "reset_peer",
            stream = "downstream",
            toxicity = action.ErrorRate,
            attributes = new { }
        };

        await PostToxicAsync(apiUrl, action.ProxyName!, toxic, cancellationToken);
        _logger.LogInformation("Injected error rate {Rate} on proxy '{Proxy}'", action.ErrorRate, action.ProxyName);
    }

    private async Task PostToxicAsync(string apiUrl, string proxyName, object toxic, CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{apiUrl}/proxies/{proxyName}/toxics", toxic, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task RemoveToxicAfterAsync(
        string proxyName,
        string toxicName,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await Task.Delay(duration, cancellationToken);

        string apiUrl = _environmentRunner.GetProxyApiUrl(proxyName);
        using HttpClient client = _httpClientFactory.CreateClient();
        await client.DeleteAsync($"{apiUrl}/proxies/{proxyName}/toxics/{toxicName}", CancellationToken.None);

        _logger.LogInformation("Removed toxic '{ToxicName}' from proxy '{Proxy}' after {Duration}",
            toxicName, proxyName, duration);
    }

    private async Task WaitForSyntheticServicesAsync(
        TimelineScenarioDefinition scenario,
        ExecutionPlan plan,
        CancellationToken cancellationToken)
    {
        HashSet<string> loadTargets = scenario.Timeline
            .SelectMany(p => p.Actions)
            .OfType<LoadAction>()
            .Select(a => a.Target)
            .ToHashSet();

        TimeSpan timeout = scenario.StartupTimeout.Value;
        Stopwatch stopwatch = Stopwatch.StartNew();

        using HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        foreach (string target in loadTargets)
        {
            string mappedAddress = _environmentRunner.GetMappedAddress(target);
            string healthUrl = $"http://{mappedAddress}/health";

            _logger.LogInformation("Waiting for '{Target}' at {Url} (timeout: {Timeout})...",
                target, healthUrl, timeout);

            while (stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    HttpResponseMessage response = await client.GetAsync(healthUrl, cancellationToken);
                    if (response.IsSuccessStatusCode) break;
                }
                catch { /* retry */ }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException($"Service '{target}' did not become available within {timeout.TotalSeconds}s.");
        }
    }
}
