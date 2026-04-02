using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class ObservabilityModel
{
    [YamlMember(Alias = "prometheus")]
    public PrometheusModel? Prometheus { get; set; }

    [YamlMember(Alias = "grafana")]
    public GrafanaModel? Grafana { get; set; }
}

public sealed class PrometheusModel
{
    [YamlMember(Alias = "port")]
    public int Port { get; set; } = 9090;

    [YamlMember(Alias = "image")]
    public string? Image { get; set; }
}

public sealed class GrafanaModel
{
    [YamlMember(Alias = "port")]
    public int Port { get; set; } = 3000;

    [YamlMember(Alias = "image")]
    public string? Image { get; set; }
}
