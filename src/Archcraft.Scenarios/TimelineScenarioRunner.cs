using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Archcraft.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Archcraft.Scenarios;

public sealed class TimelineScenarioRunner
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEnvironmentRunner _environmentRunner;
    private readonly IMetricsCollector _collector;
    private readonly ILogger<TimelineScenarioRunner> _logger;

    // Active load tasks per target key
    private readonly Dictionary<string, CancellationTokenSource> _loadCts = new();
    // Per-replica metric collectors (keyed by replica address)
    private readonly Dictionary<string, ConcurrentBag<(double LatencyMs, bool IsSuccess)>> _replicaRecords = new();

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
        _replicaRecords.Clear();

        await WaitForSyntheticServicesAsync(scenario, plan, cancellationToken);

        _logger.LogInformation("Running timeline scenario '{Name}'", scenario.Name);

        Stopwatch scenarioTimer = Stopwatch.StartNew();
        List<Task> rollbackTasks = [];

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

        await Task.WhenAll(rollbackTasks);

        foreach (CancellationTokenSource cts in _loadCts.Values)
            cts.Cancel();

        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        (double p50, double p99, double errorRate, int total, IReadOnlyList<double> raw) = _collector.GetStats();

        int targetRequests = ComputeTargetRequests(scenario);

        Dictionary<string, MetricSnapshot>? replicaSnapshots = BuildReplicaSnapshots(scenario.Name);

        return new MetricSnapshot
        {
            ScenarioName = scenario.Name,
            P50Ms = p50,
            P99Ms = p99,
            ErrorRate = errorRate,
            TotalRequests = total,
            TargetRequests = targetRequests,
            RawLatenciesMs = raw,
            ReplicaSnapshots = replicaSnapshots is { Count: > 0 } ? replicaSnapshots : null
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
                StartLoad(load);
                if (load.Duration.HasValue)
                    return StopLoadAfterAsync(load.Target, load.Duration.Value.Value, cancellationToken);
                return null;

            case InjectLatencyAction latency:
                await InjectLatencyAsync(latency, cancellationToken);
                if (latency.Duration.HasValue)
                    return RemoveToxicsAfterAsync(latency.ProxyNames, "latency-inject", latency.Duration.Value.Value, cancellationToken);
                return null;

            case InjectErrorAction error:
                await InjectErrorAsync(error, cancellationToken);
                if (error.Duration.HasValue)
                    return RemoveToxicsAfterAsync(error.ProxyNames, "error-inject", error.Duration.Value.Value, cancellationToken);
                return null;

            case KillAction kill:
                await _environmentRunner.KillReplicaAsync(kill.ResolvedReplicaName!, cancellationToken);
                if (kill.Duration.HasValue)
                    return RestoreAfterAsync(kill.ResolvedReplicaName!, kill.Duration.Value.Value, cancellationToken);
                return null;

            case RestoreAction restore:
                await _environmentRunner.RestoreReplicaAsync(restore.ResolvedReplicaName!, cancellationToken);
                return null;

            default:
                return null;
        }
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    private void StartLoad(LoadAction load)
    {
        if (_loadCts.TryGetValue(load.Target, out CancellationTokenSource? existing))
        {
            existing.Cancel();
            _loadCts.Remove(load.Target);
        }

        // Resolve all addresses for this target (single or group RR)
        IReadOnlyList<string> addresses = _environmentRunner.GetGroupAddresses(load.Target);
        string url = $"http://{addresses[0]}/{load.Endpoint}";

        CancellationTokenSource cts = new();
        _loadCts[load.Target] = cts;

        _ = RunLoadLoopAsync(addresses, load.Endpoint, load.Rps, load.RequestTimeout.Value, cts.Token);

        _logger.LogInformation("Load started: {Rps} RPS → {Target} ({Count} replica(s))",
            load.Rps, load.Target, addresses.Count);
    }

    private async Task RunLoadLoopAsync(
        IReadOnlyList<string> addresses,
        string endpoint,
        int rps,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        // Semaphore limits concurrent in-flight requests to prevent port exhaustion.
        // Cap = rps/5 (≈200ms headroom): at 1000 RPS allows ~200 concurrent connections.
        // rps*2 would allow 2000 connections which overwhelms Docker networking at high RPS.
        int maxInFlight = Math.Max(rps / 5, 50);
        using SemaphoreSlim slots = new(maxInFlight, maxInFlight);

        Stopwatch elapsed = Stopwatch.StartNew();
        long scheduled = 0;
        long rrIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Cumulative timing: how many requests should have been sent by now?
            long expected = (long)(elapsed.Elapsed.TotalSeconds * rps);

            // Cap at 1 second's worth to absorb timer jitter without runaway bursting
            long toSend = Math.Min(expected - scheduled, rps);
            scheduled += toSend;

            for (long i = 0; i < toSend && !cancellationToken.IsCancellationRequested; i++)
            {
                try { await slots.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { return; }

                string address = addresses[(int)(rrIndex % addresses.Count)];
                rrIndex++;
                string url = $"http://{address}/{endpoint}";

                _ = FireAndReleaseAsync(url, address, slots, requestTimeout, cancellationToken);
            }

            // Sleep until the next request is due
            double nextAt = (scheduled + 1.0) / rps;
            TimeSpan sleepFor = TimeSpan.FromSeconds(nextAt) - elapsed.Elapsed;

            if (sleepFor > TimeSpan.FromMilliseconds(1))
            {
                try { await Task.Delay(sleepFor, cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
            else
            {
                await Task.Yield(); // always yield to prevent synchronous monopolisation
            }
        }
    }

    private async Task FireAndReleaseAsync(
        string url,
        string replicaAddress,
        SemaphoreSlim slots,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await FireRequestAsync(url, replicaAddress, requestTimeout, cancellationToken);
        }
        finally
        {
            try { slots.Release(); }
            catch (ObjectDisposedException) { /* loop exited during in-flight drain */ }
        }
    }

    private async Task FireRequestAsync(string url, string replicaAddress, TimeSpan requestTimeout, CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = requestTimeout;

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
        RecordReplicaRequest(replicaAddress, timer.Elapsed, success);
    }

    private void RecordReplicaRequest(string replicaAddress, TimeSpan latency, bool isSuccess)
    {
        if (!_replicaRecords.TryGetValue(replicaAddress, out ConcurrentBag<(double, bool)>? bag))
        {
            bag = [];
            _replicaRecords[replicaAddress] = bag;
        }
        bag.Add((latency.TotalMilliseconds, isSuccess));
    }

    private async Task StopLoadAfterAsync(string target, TimeSpan duration, CancellationToken cancellationToken)
    {
        await Task.Delay(duration, cancellationToken);
        if (_loadCts.TryGetValue(target, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            _loadCts.Remove(target);
            _logger.LogInformation("Load stopped for '{Target}' after {Duration}", target, duration);
        }
    }

    // ── Inject ────────────────────────────────────────────────────────────────

    private async Task InjectLatencyAsync(InjectLatencyAction action, CancellationToken cancellationToken)
    {
        foreach (string proxyName in action.ProxyNames)
        {
            string apiUrl = _environmentRunner.GetProxyApiUrl(proxyName);
            object toxic = new
            {
                name = "latency-inject",
                type = "latency",
                stream = "downstream",
                toxicity = 1.0,
                attributes = new { latency = action.LatencyMs, jitter = 0 }
            };
            await PostToxicAsync(apiUrl, proxyName, toxic, cancellationToken);
            _logger.LogInformation("Injected latency {LatencyMs}ms on proxy '{Proxy}'", action.LatencyMs, proxyName);
        }
    }

    private async Task InjectErrorAsync(InjectErrorAction action, CancellationToken cancellationToken)
    {
        foreach (string proxyName in action.ProxyNames)
        {
            string apiUrl = _environmentRunner.GetProxyApiUrl(proxyName);
            object toxic = new
            {
                name = "error-inject",
                type = "reset_peer",
                stream = "downstream",
                toxicity = action.ErrorRate,
                attributes = new { }
            };
            await PostToxicAsync(apiUrl, proxyName, toxic, cancellationToken);
            _logger.LogInformation("Injected error rate {Rate} on proxy '{Proxy}'", action.ErrorRate, proxyName);
        }
    }

    private async Task PostToxicAsync(string apiUrl, string proxyName, object toxic, CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{apiUrl}/proxies/{proxyName}/toxics", toxic, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task RemoveToxicsAfterAsync(
        IReadOnlyList<string> proxyNames,
        string toxicName,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await Task.Delay(duration, cancellationToken);

        foreach (string proxyName in proxyNames)
        {
            string apiUrl = _environmentRunner.GetProxyApiUrl(proxyName);
            using HttpClient client = _httpClientFactory.CreateClient();
            await client.DeleteAsync($"{apiUrl}/proxies/{proxyName}/toxics/{toxicName}", CancellationToken.None);
            _logger.LogInformation("Removed toxic '{ToxicName}' from proxy '{Proxy}'", toxicName, proxyName);
        }
    }

    // ── Kill / Restore ────────────────────────────────────────────────────────

    private async Task RestoreAfterAsync(string replicaName, TimeSpan duration, CancellationToken cancellationToken)
    {
        await Task.Delay(duration, cancellationToken);
        await _environmentRunner.RestoreReplicaAsync(replicaName, CancellationToken.None);
        _logger.LogInformation("Auto-restored replica '{ReplicaName}' after {Duration}", replicaName, duration);
    }

    // ── Metrics ───────────────────────────────────────────────────────────────

    private Dictionary<string, MetricSnapshot>? BuildReplicaSnapshots(string scenarioName)
    {
        if (_replicaRecords.Count <= 1) return null;

        Dictionary<string, MetricSnapshot> result = new();
        foreach ((string address, ConcurrentBag<(double LatencyMs, bool IsSuccess)> bag) in _replicaRecords)
        {
            List<(double LatencyMs, bool IsSuccess)> records = [.. bag];
            if (records.Count == 0) continue;

            List<double> latencies = records.Select(r => r.LatencyMs).Order().ToList();
            int failures = records.Count(r => !r.IsSuccess);

            result[address] = new MetricSnapshot
            {
                ScenarioName = scenarioName,
                P50Ms = Percentile(latencies, 0.50),
                P99Ms = Percentile(latencies, 0.99),
                ErrorRate = (double)failures / records.Count,
                TotalRequests = records.Count,
                RawLatenciesMs = latencies
            };
        }

        return result;
    }

    private static int ComputeTargetRequests(TimelineScenarioDefinition scenario)
    {
        // Scenario ends when the last timed action completes.
        // For actions with no duration we use TimeSpan.Zero — they don't extend the scenario end.
        TimeSpan scenarioEnd = scenario.Timeline
            .Select(p => p.At + p.Actions
                .Select(a => a.Duration?.Value ?? TimeSpan.Zero)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max())
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();

        int total = 0;
        foreach (TimelinePoint point in scenario.Timeline)
        {
            foreach (TimelineAction action in point.Actions)
            {
                if (action is not LoadAction load) continue;

                TimeSpan effectiveDuration = load.Duration.HasValue
                    ? load.Duration.Value.Value
                    : scenarioEnd - point.At;

                if (effectiveDuration > TimeSpan.Zero)
                    total += load.Rps * (int)effectiveDuration.TotalSeconds;
            }
        }

        return total;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    // ── Startup wait ──────────────────────────────────────────────────────────

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
        client.Timeout = TimeSpan.FromSeconds(2);

        foreach (string target in loadTargets)
        {
            // For group targets, wait for all replicas
            IReadOnlyList<string> addresses = _environmentRunner.GetGroupAddresses(target);

            foreach (string address in addresses)
            {
                string healthUrl = $"http://{address}/health";

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
                    throw new TimeoutException(
                        $"Service '{target}' at '{address}' did not become available within {timeout.TotalSeconds}s.");
            }
        }
    }
}
