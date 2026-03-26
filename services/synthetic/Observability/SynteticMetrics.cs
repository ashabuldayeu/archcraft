using System.Diagnostics.Metrics;

namespace SynteticApi.Observability;

public sealed class SynteticMetrics : IDisposable
{
    public const string MeterName = "SynteticApi";

    private readonly Meter _meter;

    public Histogram<double> HttpRequestDuration { get; }
    public Histogram<double> OperationDuration { get; }
    public Counter<long> OperationCallsTotal { get; }

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
    }

    public void Dispose() => _meter.Dispose();
}
