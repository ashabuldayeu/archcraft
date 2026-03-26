using System.Collections.Concurrent;
using Archcraft.Contracts;

namespace Archcraft.Observability;

public sealed class InMemoryMetricsCollector : IMetricsCollector
{
    private readonly ConcurrentBag<(double LatencyMs, bool IsSuccess)> _records = [];

    public void RecordRequest(TimeSpan latency, bool isSuccess) =>
        _records.Add((latency.TotalMilliseconds, isSuccess));

    public (double P50Ms, double P99Ms, double ErrorRate, int TotalRequests, IReadOnlyList<double> RawLatenciesMs) GetStats()
    {
        List<(double LatencyMs, bool IsSuccess)> snapshot = [.. _records];

        if (snapshot.Count == 0)
            return (0, 0, 0, 0, []);

        List<double> latencies = snapshot.Select(r => r.LatencyMs).Order().ToList();
        int total = latencies.Count;
        int failures = snapshot.Count(r => !r.IsSuccess);

        double p50 = Percentile(latencies, 0.50);
        double p99 = Percentile(latencies, 0.99);
        double errorRate = (double)failures / total;

        return (p50, p99, errorRate, total, latencies);
    }

    public void Reset() => _records.Clear();

    private static double Percentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        int index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
