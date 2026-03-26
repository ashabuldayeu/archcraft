namespace Archcraft.Contracts;

public interface IMetricsCollector
{
    void RecordRequest(TimeSpan latency, bool isSuccess);
    (double P50Ms, double P99Ms, double ErrorRate, int TotalRequests, IReadOnlyList<double> RawLatenciesMs) GetStats();
    void Reset();
}
