using System.Diagnostics;
using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Archcraft.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Archcraft.Scenarios;

public sealed class HttpScenarioRunner : IScenarioRunner
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpScenarioRunner> _logger;

    public HttpScenarioRunner(IHttpClientFactory httpClientFactory, ILogger<HttpScenarioRunner> logger)
    {
        _httpClientFactory = httpClientFactory;
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
        client.Timeout = TimeSpan.FromSeconds(5);

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
        TimeSpan interval = TimeSpan.FromMilliseconds(1000.0 / rps);

        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < duration && !cancellationToken.IsCancellationRequested)
        {
            Stopwatch requestTimer = Stopwatch.StartNew();
            _ = FireRequestAsync(scenario.Target, collector, cancellationToken);

            TimeSpan waitTime = interval - requestTimer.Elapsed;
            if (waitTime > TimeSpan.Zero)
                await Task.Delay(waitTime, cancellationToken);
        }

        // Wait briefly for in-flight requests to settle
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
    }

    private async Task FireRequestAsync(string target, IMetricsCollector collector, CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

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
}
