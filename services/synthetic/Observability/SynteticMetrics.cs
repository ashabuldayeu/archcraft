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

    /// <summary>
    /// Outbound edge call duration. Tags: client (this service), operation (e.g. "redis-call").
    /// Exported as archcraft_edge_duration_seconds in Prometheus.
    /// </summary>
    public Histogram<double> EdgeDuration { get; }

    /// <summary>
    /// Outbound edge call counter. Tags: client, operation, status ("ok" | "error" | "not_found").
    /// Exported as archcraft_edge_requests_total in Prometheus.
    /// </summary>
    public Counter<long> EdgeRequestsTotal { get; }

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

        EdgeDuration = _meter.CreateHistogram<double>(
            "archcraft.edge.duration",
            unit: "s",
            description: "Duration of outbound adapter calls per operation");

        EdgeRequestsTotal = _meter.CreateCounter<long>(
            "archcraft.edge.requests",
            description: "Total outbound adapter calls per operation and status");
    }

    public void Dispose() => _meter.Dispose();
}
