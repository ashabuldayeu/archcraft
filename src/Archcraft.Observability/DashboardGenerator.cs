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

        WritePrometheusConfig(dashboardsDir, plan);
        WriteGrafanaDatasource(dashboardsDir);
        WriteGrafanaDashboardProvisioning(dashboardsDir);

        ObservabilityDefinition obs = plan.Observability;

        // Group synthetic services by service group (or own name for non-replicated)
        List<SyntheticGroup> syntheticGroups = plan.OrderedServices
            .Where(s => s.SyntheticEndpoints.Count > 0)
            .GroupBy(s => s.ServiceGroup ?? s.Name)
            .Select(g => new SyntheticGroup(g.Key, g.Select(s => s.Name).ToList()))
            .ToList();

        // Aggregate dashboard per service group
        foreach (SyntheticGroup group in syntheticGroups)
            WriteGroupDashboard(dashboardsDir, group);

        // Exporter dashboards (postgres, redis, kafka)
        foreach (ExporterDefinition exporter in obs.Exporters)
        {
            if (exporter.Technology == "redis")
                WriteDashboard(dashboardsDir, "Dashboards.redis.dashboard.json", exporter.ServiceName, $"redis-{exporter.ServiceName}.json");
            else if (exporter.Technology == "postgres")
                WriteDashboard(dashboardsDir, "Dashboards.postgres.dashboard.json", exporter.ServiceName, $"postgres-{exporter.ServiceName}.json");
            else if (exporter.Technology == "kafka")
                WriteDashboard(dashboardsDir, "Dashboards.kafka.dashboard.json", exporter.ServiceName, $"kafka-{exporter.ServiceName}.json");
            else if (exporter.Technology == "rabbitmq")
                WriteDashboard(dashboardsDir, "Dashboards.rabbitmq.dashboard.json", exporter.ServiceName, $"rabbitmq-{exporter.ServiceName}.json");
        }

        // Project-wide overview dashboard
        WriteOverviewDashboard(dashboardsDir, plan, syntheticGroups);

        return Task.CompletedTask;
    }

    private static void WriteGroupDashboard(string dashboardsDir, SyntheticGroup group)
    {
        string template = ReadEmbeddedResource("Dashboards.synthetic-group.dashboard.json");
        string jobRegex = string.Join("|", group.Instances);
        string content = template
            .Replace("SERVICE_NAME", group.Name)
            .Replace("JOB_REGEX", jobRegex);
        File.WriteAllText(Path.Combine(dashboardsDir, $"{group.Name}.json"), content);
    }

    private static void WriteDashboard(string dashboardsDir, string resourceSuffix, string serviceName, string outputFileName)
    {
        string template = ReadEmbeddedResource(resourceSuffix);
        string content = template.Replace("SERVICE_NAME", serviceName);
        File.WriteAllText(Path.Combine(dashboardsDir, outputFileName), content);
    }

    private static void WriteOverviewDashboard(
        string dashboardsDir,
        ExecutionPlan plan,
        List<SyntheticGroup> syntheticGroups)
    {
        StringBuilder sb = new();
        int panelId = 1;
        int y = 0;

        sb.AppendLine("{");
        sb.AppendLine("  \"id\": null,");
        sb.AppendLine($"  \"uid\": \"overview-{plan.ProjectName.ToLowerInvariant().Replace(' ', '-')}\",");
        sb.AppendLine($"  \"title\": \"{plan.ProjectName} — Overview\",");
        sb.AppendLine("  \"schemaVersion\": 38,");
        sb.AppendLine("  \"refresh\": \"5s\",");
        sb.AppendLine("  \"time\": { \"from\": \"now-15m\", \"to\": \"now\" },");
        sb.AppendLine("  \"annotations\": {");
        sb.AppendLine("    \"list\": [");
        sb.AppendLine("      {");
        sb.AppendLine("        \"builtIn\": 1,");
        sb.AppendLine("        \"datasource\": { \"type\": \"grafana\", \"uid\": \"-- Grafana --\" },");
        sb.AppendLine("        \"enable\": true,");
        sb.AppendLine("        \"hide\": false,");
        sb.AppendLine("        \"iconColor\": \"rgba(0, 211, 255, 1)\",");
        sb.AppendLine("        \"name\": \"Archcraft Events\",");
        sb.AppendLine("        \"type\": \"tags\",");
        sb.AppendLine("        \"tags\": [\"archcraft\"]");
        sb.AppendLine("      }");
        sb.AppendLine("    ]");
        sb.AppendLine("  },");
        sb.AppendLine("  \"panels\": [");

        List<string> panels = [];

        // ── Synthetic services ──────────────────────────────────────────────────

        foreach (SyntheticGroup group in syntheticGroups)
        {
            string jobRegex = string.Join("|", group.Instances);

            panels.Add(OverviewPanel(panelId++, $"{group.Name} — RPS (req/s)", "reqps", 8, 0, y,
                $"sum(rate(http_server_request_duration_seconds_count{{job=~\\\"{jobRegex}\\\"}}[1m]))", "total"));

            panels.Add(OverviewPanel(panelId++, $"{group.Name} — p99 Latency (ms)", "ms", 8, 8, y,
                $"histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket{{job=~\\\"{jobRegex}\\\"}}[1m]))) * 1000", "p99"));

            panels.Add(OverviewErrorPanel(panelId++, $"{group.Name} — Error Rate (req/s)", 8, 16, y,
                $"sum(rate(http_server_request_duration_seconds_count{{job=~\\\"{jobRegex}\\\", http_response_status_code=~\\\"5..\\\"}}[1m]))", "errors/s"));

            y += 8;
        }

        // ── Infrastructure ──────────────────────────────────────────────────────

        if (plan.Observability is not null)
        {
            foreach (ExporterDefinition exporter in plan.Observability.Exporters)
            {
                string job = $"{exporter.ServiceName}-exporter";

                if (exporter.Technology == "postgres")
                {
                    panels.Add(OverviewPanel(panelId++, $"{exporter.ServiceName} — Active Connections", "short", 12, 0, y,
                        $"sum(pg_stat_database_numbackends{{job=\\\"{job}\\\"}})", "connections"));

                    panels.Add(OverviewPanel(panelId++, $"{exporter.ServiceName} — Max Query Duration (ms)", "ms", 12, 12, y,
                        $"pg_stat_activity_max_tx_duration{{job=\\\"{job}\\\", state=\\\"active\\\"}} * 1000", "active (ms)"));

                    y += 8;
                }
                else if (exporter.Technology == "redis")
                {
                    panels.Add(OverviewPanel(panelId++, $"{exporter.ServiceName} — Ops / sec", "ops", 12, 0, y,
                        $"sum(rate(redis_commands_processed_total{{job=\\\"{job}\\\"}}[1m]))", "ops/s"));

                    panels.Add(OverviewPanel(panelId++, $"{exporter.ServiceName} — Memory Used (bytes)", "bytes", 12, 12, y,
                        $"redis_memory_used_bytes{{job=\\\"{job}\\\"}}", "memory"));

                    y += 8;
                }
                else if (exporter.Technology == "kafka")
                {
                    panels.Add(OverviewPanel(panelId++, $"{exporter.ServiceName} — Consumer Lag (total)", "short", 12, 0, y,
                        $"sum(kafka_consumergroup_lag{{job=\\\"{job}\\\"}})", "lag"));

                    panels.Add(OverviewPanel(panelId++, $"{exporter.ServiceName} — Messages Produced / sec", "msgps", 12, 12, y,
                        $"sum(rate(kafka_topic_partition_current_offset{{job=\\\"{job}\\\", topic!~\\\"__.*\\\"}}[1m]))", "msg/s"));

                    y += 8;
                }
                else if (exporter.Technology == "rabbitmq")
                {
                    panels.Add(OverviewPanel(panelId++, $"{exporter.ServiceName} — Messages Ready", "short", 12, 0, y,
                        $"sum(rabbitmq_queue_messages_ready{{job=\\\"{job}\\\"}})", "ready"));

                    panels.Add(OverviewPanel(panelId++, $"{exporter.ServiceName} — Deliver Rate (msg/s)", "msgps", 12, 12, y,
                        $"sum(rate(rabbitmq_queue_messages_delivered_total{{job=\\\"{job}\\\"}}[1m]))", "deliver/s"));

                    y += 8;
                }
            }
        }

        // ── Topology edge panels ────────────────────────────────────────────────

        List<string> allOperations = plan.OrderedServices
            .SelectMany(s => s.SyntheticOperations)
            .Distinct()
            .ToList();

        if (allOperations.Count > 0)
        {
            panels.Add(TopologyLatencyPanel(panelId++, y));
            panels.Add(TopologyErrorPanel(panelId++, y));
            y += 8;
        }

        sb.Append(string.Join(",\n", panels));
        sb.AppendLine();
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(dashboardsDir, "_overview.json"), sb.ToString());
    }

    private static string OverviewPanel(int id, string title, string unit, int w, int x, int y, string expr, string legend) =>
        $$"""
            {
              "id": {{id}},
              "type": "timeseries",
              "title": "{{title}}",
              "gridPos": { "h": 8, "w": {{w}}, "x": {{x}}, "y": {{y}} },
              "fieldConfig": { "defaults": { "unit": "{{unit}}", "min": 0 } },
              "targets": [
                {
                  "datasource": "Prometheus",
                  "expr": "{{expr}}",
                  "legendFormat": "{{legend}}"
                }
              ]
            }
        """;

    private static string OverviewErrorPanel(int id, string title, int w, int x, int y, string expr, string legend) =>
        $$"""
            {
              "id": {{id}},
              "type": "timeseries",
              "title": "{{title}}",
              "gridPos": { "h": 8, "w": {{w}}, "x": {{x}}, "y": {{y}} },
              "fieldConfig": { "defaults": { "unit": "reqps", "min": 0, "color": { "fixedColor": "red", "mode": "fixed" } } },
              "targets": [
                {
                  "datasource": "Prometheus",
                  "expr": "{{expr}}",
                  "legendFormat": "{{legend}}"
                }
              ]
            }
        """;

    private static string TopologyLatencyPanel(int id, int y)
    {
        string opLabel = "{{operation}}";
        return $$"""
            {
              "id": {{id}},
              "type": "timeseries",
              "title": "Service Topology — Edge p99 Latency (ms)",
              "description": "p99 latency per outbound operation across all synthetic service instances. A spike on a specific operation pinpoints which dependency is the bottleneck.",
              "gridPos": { "h": 8, "w": 12, "x": 0, "y": {{y}} },
              "fieldConfig": { "defaults": { "unit": "ms", "min": 0 } },
              "targets": [
                {
                  "datasource": "Prometheus",
                  "expr": "histogram_quantile(0.99, sum by (le, operation) (rate(archcraft_edge_duration_seconds_bucket[1m]))) * 1000",
                  "legendFormat": "{{opLabel}} p99"
                },
                {
                  "datasource": "Prometheus",
                  "expr": "histogram_quantile(0.5, sum by (le, operation) (rate(archcraft_edge_duration_seconds_bucket[1m]))) * 1000",
                  "legendFormat": "{{opLabel}} p50"
                }
              ]
            }
        """;
    }

    private static string TopologyErrorPanel(int id, int y)
    {
        string opLabel = "{{operation}}";
        return $$"""
            {
              "id": {{id}},
              "type": "timeseries",
              "title": "Service Topology — Edge Error Rate % per Operation",
              "description": "Error + not_found rate per outbound operation. Identifies which dependency is failing and on which service group.",
              "gridPos": { "h": 8, "w": 12, "x": 12, "y": {{y}} },
              "fieldConfig": {
                "defaults": {
                  "unit": "percent", "min": 0, "max": 100,
                  "thresholds": {
                    "mode": "absolute",
                    "steps": [
                      { "color": "green",  "value": null },
                      { "color": "yellow", "value": 1 },
                      { "color": "red",    "value": 5 }
                    ]
                  }
                }
              },
              "targets": [
                {
                  "datasource": "Prometheus",
                  "expr": "(sum by (operation) (rate(archcraft_edge_requests_total{status=~\"error|not_found\"}[1m])) or (sum by (operation) (rate(archcraft_edge_requests_total[1m])) * 0)) / sum by (operation) (rate(archcraft_edge_requests_total[1m])) * 100",
                  "legendFormat": "{{opLabel}}"
                }
              ]
            }
        """;
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

    private sealed record SyntheticGroup(string Name, List<string> Instances);
}
