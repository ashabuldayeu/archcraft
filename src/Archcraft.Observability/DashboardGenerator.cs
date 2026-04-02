using System.Reflection;
using System.Text;
using Archcraft.Domain.Entities;

namespace Archcraft.Observability;

public sealed class DashboardGenerator
{
    private static readonly Assembly ThisAssembly = typeof(DashboardGenerator).Assembly;

    public Task GenerateAsync(ExecutionPlan plan, string projectDirectory)
    {
        if (plan.Observability is null)
            return Task.CompletedTask;

        string dashboardsDir = Path.Combine(projectDirectory, "dashboards");
        Directory.CreateDirectory(dashboardsDir);

        ObservabilityDefinition obs = plan.Observability;

        WritePrometheusConfig(dashboardsDir, plan);
        WriteGrafanaDatasource(dashboardsDir);
        WriteGrafanaDashboardProvisioning(dashboardsDir);

        IReadOnlyList<ServiceDefinition> syntheticServices = plan.OrderedServices
            .Where(s => s.SyntheticEndpoints.Count > 0)
            .ToList();

        foreach (ServiceDefinition service in syntheticServices)
            WriteDashboard(dashboardsDir, "Dashboards.synthetic.dashboard.json", service.Name, $"{service.Name}.json");

        foreach (ExporterDefinition exporter in obs.Exporters)
        {
            if (exporter.Technology == "redis")
                WriteDashboard(dashboardsDir, "Dashboards.redis.dashboard.json", exporter.ServiceName, $"redis-{exporter.ServiceName}.json");
            else if (exporter.Technology == "postgres")
                WriteDashboard(dashboardsDir, "Dashboards.postgres.dashboard.json", exporter.ServiceName, $"postgres-{exporter.ServiceName}.json");
        }

        return Task.CompletedTask;
    }

    private static void WriteDashboard(string dashboardsDir, string resourceSuffix, string serviceName, string outputFileName)
    {
        string template = ReadEmbeddedResource(resourceSuffix);
        string content = template.Replace("SERVICE_NAME", serviceName);
        File.WriteAllText(Path.Combine(dashboardsDir, outputFileName), content);
    }

    private static string ReadEmbeddedResource(string resourceSuffix)
    {
        string resourceName = $"Archcraft.Observability.{resourceSuffix}";
        using Stream stream = ThisAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static void WritePrometheusConfig(string dashboardsDir, ExecutionPlan plan)
    {
        ObservabilityDefinition obs = plan.Observability!;
        StringBuilder sb = new();

        sb.AppendLine("global:");
        sb.AppendLine("  scrape_interval: 5s");
        sb.AppendLine("  evaluation_interval: 5s");
        sb.AppendLine();
        sb.AppendLine("scrape_configs:");

        IReadOnlyList<ServiceDefinition> syntheticServices = plan.OrderedServices
            .Where(s => s.SyntheticEndpoints.Count > 0)
            .ToList();

        foreach (ServiceDefinition service in syntheticServices)
        {
            sb.AppendLine($"  - job_name: '{service.Name}'");
            sb.AppendLine("    static_configs:");
            sb.AppendLine($"      - targets: ['{service.Name}:{service.Port.Value}']");
            sb.AppendLine("    metrics_path: '/metrics'");
        }

        foreach (ExporterDefinition exporter in obs.Exporters)
        {
            sb.AppendLine($"  - job_name: '{exporter.Name}'");
            sb.AppendLine("    static_configs:");
            sb.AppendLine($"      - targets: ['{exporter.Name}:{exporter.ExporterPort}']");
        }

        File.WriteAllText(Path.Combine(dashboardsDir, "prometheus.yml"), sb.ToString());
    }

    private static void WriteGrafanaDatasource(string dashboardsDir)
    {
        string provDir = Path.Combine(dashboardsDir, "provisioning", "datasources");
        Directory.CreateDirectory(provDir);
        File.WriteAllText(Path.Combine(provDir, "datasource.yml"),
            ReadEmbeddedResource("Templates.grafana-datasource.yml"));
    }

    private static void WriteGrafanaDashboardProvisioning(string dashboardsDir)
    {
        string provDir = Path.Combine(dashboardsDir, "provisioning", "dashboards");
        Directory.CreateDirectory(provDir);
        File.WriteAllText(Path.Combine(provDir, "dashboards.yml"),
            ReadEmbeddedResource("Templates.grafana-dashboards.yml"));
    }
}
