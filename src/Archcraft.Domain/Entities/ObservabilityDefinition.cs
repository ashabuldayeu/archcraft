namespace Archcraft.Domain.Entities;

public sealed record ObservabilityDefinition
{
    public required PrometheusConfig Prometheus { get; init; }
    public required GrafanaConfig Grafana { get; init; }
    public IReadOnlyList<ExporterDefinition> Exporters { get; init; } = [];
}

public sealed record PrometheusConfig
{
    public required int Port { get; init; }
    public string Image { get; init; } = "prom/prometheus:v3.2.1";
}

public sealed record GrafanaConfig
{
    public required int Port { get; init; }
    public string Image { get; init; } = "grafana/grafana:11.5.2";
}

public sealed record ExporterDefinition
{
    public required string Name { get; init; }
    public required string Image { get; init; }
    public required string ServiceName { get; init; }
    public required string Technology { get; init; }
    public required int ExporterPort { get; init; }
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
}
