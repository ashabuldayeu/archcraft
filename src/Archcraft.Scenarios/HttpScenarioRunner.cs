using System.Diagnostics;
using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Archcraft.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Archcraft.Scenarios;

public sealed class HttpScenarioRunner : IScenarioRunner
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEnvironmentRunner _environmentRunner;
    private readonly ILogger<HttpScenarioRunner> _logger;

    public HttpScenarioRunner(
        IHttpClientFactory httpClientFactory,
        IEnvironmentRunner environmentRunner,
        ILogger<HttpScenarioRunner> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environmentRunner = environmentRunner;
        _logger = logger;
    }

    public bool CanHandle(ScenarioDefinition scenario) => scenario.Type == ScenarioType.Http;

    public async Task<MetricSnapshot> RunAsync(
        ScenarioDefinition scenario,
        IMetricsCollector collector,
        CancellationToken cancellationToken = default)
    {
        collector.Reset();

        await WaitForTargetAsync(scenario, cancellationToken);

        _logger.LogInformation(
            "Running scenario '{Name}': {Rps} RPS → {Target} for {Duration}",
            scenario.Name, scenario.Rps.Value, scenario.Target, scenario.ScenarioDuration.Value);

        await RunLoadAsync(scenario, collector, cancellationToken);

        if (scenario.RestartAfter.Count > 0)
            await RestartContainersAsync(scenario.RestartAfter, cancellationToken);

        (double p50, double p99, double errorRate, int total, IReadOnlyList<double> raw) = collector.GetStats();

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

    private async Task WaitForTargetAsync(ScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(2);

        TimeSpan timeout = scenario.StartupTimeout.Value;
        Stopwatch stopwatch = Stopwatch.StartNew();

        string healthUrl = BuildHealthUrl(scenario.Target);
        _logger.LogInformation("Waiting for target {Target} to become available (timeout: {Timeout})...",
            scenario.Target, timeout);

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                HttpResponseMessage response = await client.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Target {Target} is ready.", scenario.Target);
                    return;
                }
            }
            catch { /* retry */ }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException(
            $"Target '{scenario.Target}' did not become available within {timeout.TotalSeconds}s.");
    }

    private static string BuildHealthUrl(string target)
    {
        Uri uri = new(target);
        return $"{uri.Scheme}://{uri.Authority}/health";
    }

    private async Task RunLoadAsync(
        ScenarioDefinition scenario,
        IMetricsCollector collector,
        CancellationToken cancellationToken)
    {
        TimeSpan duration = scenario.ScenarioDuration.Value;
        int rps = scenario.Rps.Value;

        // Drain CTS — controls in-flight requests; cancelled only when drain timeout expires
        using CancellationTokenSource drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Semaphore limits concurrent in-flight requests to prevent port exhaustion.
        // Cap = rps/5 (≈200ms headroom): at 1000 RPS allows ~200 concurrent connections.
        // rps*2 would allow 2000 connections which overwhelms Docker networking at high RPS.
        int maxInFlight = Math.Max(rps / 5, 50);
        using SemaphoreSlim slots = new(maxInFlight, maxInFlight);

        List<Task> pending = [];
        Stopwatch elapsed = Stopwatch.StartNew();
        long scheduled = 0;

        // ── Load phase ────────────────────────────────────────────────────────
        bool cancelled = false;
        while (elapsed.Elapsed < duration && !cancellationToken.IsCancellationRequested && !cancelled)
        {
            // Cumulative timing: how many requests should have been sent by now?
            long expected = (long)(elapsed.Elapsed.TotalSeconds * rps);

            // Cap at 1 second's worth to absorb timer jitter without runaway bursting
            long toSend = Math.Min(expected - scheduled, rps);
            scheduled += toSend;

            for (long i = 0; i < toSend && !cancelled; i++)
            {
                try { await slots.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { cancelled = true; break; }

                pending.Add(FireAndReleaseAsync(scenario.Target, collector, slots, scenario.RequestTimeout.Value, drainCts.Token));
            }

            // Sleep until the next request is due
            double nextAt = (scheduled + 1.0) / rps;
            TimeSpan sleepFor = TimeSpan.FromSeconds(nextAt) - elapsed.Elapsed;

            if (sleepFor > TimeSpan.FromMilliseconds(1))
            {
                try { await Task.Delay(sleepFor, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
            else
            {
                await Task.Yield(); // always yield to prevent synchronous monopolisation
            }
        }

        // ── Drain phase ───────────────────────────────────────────────────────
        if (scenario.DrainTimeout is null)
        {
            // Legacy behaviour: brief fixed wait, no explicit drain
            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
            return;
        }

        _logger.LogInformation(
            "Scenario '{Name}': load phase done, draining {Count} in-flight request(s) (timeout: {Timeout})...",
            scenario.Name, pending.Count(t => !t.IsCompleted), scenario.DrainTimeout.Value.Value);

        try
        {
            using CancellationTokenSource timeoutCts = new(scenario.DrainTimeout.Value.Value);
            await Task.WhenAll(pending).WaitAsync(timeoutCts.Token);
            _logger.LogInformation("Scenario '{Name}': all in-flight requests completed.", scenario.Name);
        }
        catch (OperationCanceledException)
        {
            int remaining = pending.Count(t => !t.IsCompleted);
            _logger.LogWarning(
                "Scenario '{Name}': drain timeout expired, cancelling {Count} remaining request(s).",
                scenario.Name, remaining);
            drainCts.Cancel();

            // Give cancellation a moment to propagate before collecting stats
            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        }
    }

    private async Task FireAndReleaseAsync(
        string target,
        IMetricsCollector collector,
        SemaphoreSlim slots,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await FireRequestAsync(target, collector, requestTimeout, cancellationToken);
        }
        finally
        {
            try { slots.Release(); }
            catch (ObjectDisposedException) { /* scenario ended during drain */ }
        }
    }

    private async Task FireRequestAsync(string target, IMetricsCollector collector, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = timeout;

        Stopwatch timer = Stopwatch.StartNew();
        bool success = false;

        try
        {
            HttpResponseMessage response = await client.PostAsync(target, content: null, cancellationToken);
            timer.Stop();
            success = response.IsSuccessStatusCode;
        }
        catch
        {
            timer.Stop();
            success = false;
        }

        collector.RecordRequest(timer.Elapsed, success);
    }

    private async Task RestartContainersAsync(IReadOnlyList<string> aliases, CancellationToken cancellationToken)
    {
        foreach (string alias in aliases)
        {
            _logger.LogInformation("Post-scenario restart: '{Alias}'...", alias);
            await _environmentRunner.RestartContainerAsync(alias, cancellationToken);
        }
    }
}
