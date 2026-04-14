using System.Diagnostics.Metrics;

namespace SynteticApi.Observability;

public sealed class SynteticMetrics : IDisposable
{
    public const string MeterName = "SynteticApi";

    private readonly Meter _meter;

    public Histogram<double> HttpRequestDuration { get; }
    public Histogram<double> OperationDuration { get; }
    public Counter<long> OperationCallsTotal { get; }

    /// <summary>
    /// Per-endpoint request counter. Tags: alias, status ("ok" | "error").
    /// Exported as synthetic_requests_total in Prometheus.
    /// </summary>
    public Counter<long> RequestsTotal { get; }

    public SynteticMetrics(string serviceName)
    {
        _meter = new Meter(MeterName, "1.0.0");

        HttpRequestDuration = _meter.CreateHistogram<double>(
            "http.server.request.duration",
            unit: "s",
            description: "Duration of HTTP server requests");

        OperationDuration = _meter.CreateHistogram<double>(
            "operation.duration",
            unit: "s",
            description: "Duration of pipeline operations");

        OperationCallsTotal = _meter.CreateCounter<long>(
            "operation.calls.total",
            description: "Total number of pipeline operation calls");

        RequestsTotal = _meter.CreateCounter<long>(
            "synthetic.requests",
            description: "Total requests handled per endpoint alias and status");
    }

    public void Dispose() => _meter.Dispose();
}
