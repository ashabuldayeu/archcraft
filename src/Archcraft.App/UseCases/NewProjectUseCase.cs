using System.Text;

namespace Archcraft.App.UseCases;

public sealed class NewProjectUseCase
{
    public async Task<int> ExecuteAsync(
        string projectName,
        int services,
        string db,
        int replicas,
        CancellationToken cancellationToken = default)
    {
        string projectFilePath = Path.Combine(Directory.GetCurrentDirectory(), "project.yaml");

        if (File.Exists(projectFilePath))
        {
            Console.Write("File project.yaml already exists. Overwrite? [y/N] ");
            string? answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted.");
                return 1;
            }
        }

        string yaml = BuildYaml(projectName, services, db, replicas);
        await File.WriteAllTextAsync(projectFilePath, yaml, Encoding.UTF8, cancellationToken);

        string resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
        string dashboardsDir = Path.Combine(Directory.GetCurrentDirectory(), "dashboards");
        Directory.CreateDirectory(resultsDir);
        Directory.CreateDirectory(dashboardsDir);
        await File.WriteAllTextAsync(Path.Combine(resultsDir, ".gitkeep"), "", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dashboardsDir, ".gitkeep"), "", cancellationToken);

        Console.WriteLine($"Created project.yaml ({projectName})");
        Console.WriteLine($"Created results/");
        Console.WriteLine($"Created dashboards/");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  archcraft validate project.yaml");
        Console.WriteLine("  archcraft run project.yaml");

        return 0;
    }

    private static string BuildYaml(string projectName, int serviceCount, string db, int replicas)
    {
        StringBuilder sb = new();

        // ── Header ────────────────────────────────────────────────────────────
        sb.AppendLine($"name: {projectName}");
        sb.AppendLine();

        // ── Services ──────────────────────────────────────────────────────────
        sb.AppendLine("# ── Инфраструктурные сервисы ──────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("services:");

        bool hasDb = db != "none";

        if (db == "postgres")
        {
            sb.AppendLine("  - name: postgres");
            sb.AppendLine("    image: bitnami/postgresql:latest");
            sb.AppendLine("    port: 5432");
            sb.AppendLine("    proxy: pg-proxy");
            sb.AppendLine("    cluster:");
            sb.AppendLine("      replicas: 1");
            sb.AppendLine("      replication_user: replicator");
            sb.AppendLine("      replication_password: repl_secret");
            sb.AppendLine("    env:");
            sb.AppendLine("      POSTGRESQL_DATABASE: archcraft");
            sb.AppendLine("      POSTGRESQL_USERNAME: user");
            sb.AppendLine("      POSTGRESQL_PASSWORD: secret");
            sb.AppendLine("      POSTGRESQL_MAX_CONNECTIONS: \"200\"");
            sb.AppendLine();
        }
        else if (db == "redis")
        {
            sb.AppendLine("  - name: redis");
            sb.AppendLine("    image: bitnami/redis:latest");
            sb.AppendLine("    port: 6379");
            sb.AppendLine("    proxy: redis-proxy");
            sb.AppendLine("    cluster:");
            sb.AppendLine("      replicas: 1");
            sb.AppendLine("      replication_password: repl_secret");
            sb.AppendLine();
        }

        // Synthetic services
        sb.AppendLine("# ── Synthetic-сервисы ────────────────────────────────────────────────────");
        sb.AppendLine();

        List<string> syntheticNames = BuildServiceNames(serviceCount);

        for (int i = 0; i < syntheticNames.Count; i++)
        {
            string svcName = syntheticNames[i];
            bool isLast = i == syntheticNames.Count - 1;

            // Determine what this service calls
            string? nextSvc = isLast ? (hasDb ? db : null) : syntheticNames[i + 1];
            string adapterName = nextSvc is null ? "http-adapter" : $"{nextSvc}-adapter";
            string operationName = nextSvc is null ? "http-call" : $"{nextSvc}-call";

            sb.AppendLine($"  - name: {svcName}");
            sb.AppendLine("    image: archcraft/synthetic:latest");
            sb.AppendLine("    port: 8080");
            sb.AppendLine($"    replicas: {replicas}");
            sb.AppendLine($"    proxy: {svcName}-proxy");
            sb.AppendLine("    readiness:");
            sb.AppendLine("      path: /health");
            sb.AppendLine("      timeout: 30s");
            sb.AppendLine("    synthetic:");
            sb.AppendLine("      adapters:");
            sb.AppendLine($"        - {adapterName}");
            sb.AppendLine("      endpoints:");

            if (i == 0)
            {
                // Frontend / entry point endpoint
                sb.AppendLine("        - alias: handle");
                sb.AppendLine("          pipeline:");
                sb.AppendLine($"            - operation: {operationName}");
            }
            else
            {
                // Downstream synthetic service
                sb.AppendLine("        - alias: process");
                sb.AppendLine("          pipeline:");
                sb.AppendLine($"            - operation: {operationName}");
            }

            sb.AppendLine();
        }

        // ── Adapters ──────────────────────────────────────────────────────────
        sb.AppendLine("# ── Адаптеры ─────────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("adapters:");

        if (db == "postgres")
        {
            string lastSvc = syntheticNames.Last();
            sb.AppendLine($"  - name: postgres-adapter");
            sb.AppendLine("    image: archcraft/pg-adapter:latest");
            sb.AppendLine("    port: 8080");
            sb.AppendLine("    technology: postgres");
            sb.AppendLine("    connects_to: postgres");
            sb.AppendLine("    seed_rows: 10000");
            sb.AppendLine();
        }
        else if (db == "redis")
        {
            sb.AppendLine($"  - name: redis-adapter");
            sb.AppendLine("    image: archcraft/redis-adapter:latest");
            sb.AppendLine("    port: 8080");
            sb.AppendLine("    technology: redis");
            sb.AppendLine("    connects_to: redis");
            sb.AppendLine("    seed_rows: 10000");
            sb.AppendLine();
        }

        // Inter-service http adapters (all except the last synthetic which uses db adapter)
        for (int i = 0; i < syntheticNames.Count - 1; i++)
        {
            string nextSvc = syntheticNames[i + 1];
            sb.AppendLine($"  - name: {nextSvc}-adapter");
            sb.AppendLine("    image: archcraft/http-adapter:latest");
            sb.AppendLine("    port: 8080");
            sb.AppendLine("    technology: http");
            sb.AppendLine($"    connects_to: {nextSvc}");
            sb.AppendLine();
        }

        // ── Connections ───────────────────────────────────────────────────────
        sb.AppendLine("# ── Связи между сервисами ────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("connections:");

        for (int i = 0; i < syntheticNames.Count - 1; i++)
        {
            string from = syntheticNames[i];
            string to = syntheticNames[i + 1];
            string adapterName = $"{to}-adapter";
            sb.AppendLine($"  - from: {from}");
            sb.AppendLine($"    to: {to}");
            sb.AppendLine("    protocol: http");
            sb.AppendLine("    port: 8080");
            sb.AppendLine($"    via: {adapterName}");
            sb.AppendLine();
        }

        if (db == "postgres")
        {
            string lastSvc = syntheticNames.Last();
            sb.AppendLine($"  - from: {lastSvc}");
            sb.AppendLine("    to: postgres");
            sb.AppendLine("    protocol: tcp");
            sb.AppendLine("    port: 5432");
            sb.AppendLine("    via: postgres-adapter");
            sb.AppendLine();
        }
        else if (db == "redis")
        {
            string lastSvc = syntheticNames.Last();
            sb.AppendLine($"  - from: {lastSvc}");
            sb.AppendLine("    to: redis");
            sb.AppendLine("    protocol: tcp");
            sb.AppendLine("    port: 6379");
            sb.AppendLine("    via: redis-adapter");
            sb.AppendLine();
        }

        // ── Scenarios ─────────────────────────────────────────────────────────
        sb.AppendLine("# ── Сценарии ─────────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("scenarios:");
        sb.AppendLine("  # Прогрев — устанавливает соединения и прогревает кэши");
        sb.AppendLine("  - name: warmup");
        sb.AppendLine("    startup_timeout: 120s");
        sb.AppendLine("    timeline:");
        sb.AppendLine("      - at: 0s");
        sb.AppendLine("        actions:");
        sb.AppendLine($"          - type: load");
        sb.AppendLine($"            target: {syntheticNames[0]}");
        sb.AppendLine("            endpoint: handle");
        sb.AppendLine("            rps: 500");
        sb.AppendLine("            duration: 20s");
        sb.AppendLine();

        // ── Observability ─────────────────────────────────────────────────────
        sb.AppendLine("# ── Observability ────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("observability:");
        sb.AppendLine("  prometheus:");
        sb.AppendLine("    port: 9090");
        sb.AppendLine("  grafana:");
        sb.AppendLine("    port: 3000");

        return sb.ToString();
    }

    /// <summary>
    /// Returns synthetic service names for a given count.
    /// 1 service  → ["frontend"]
    /// 2 services → ["frontend", "backend"]
    /// 3+ services → ["frontend", "svc-1", ..., "svc-{n-1}"]
    /// </summary>
    private static List<string> BuildServiceNames(int count) => count switch
    {
        1 => ["frontend"],
        2 => ["frontend", "backend"],
        _ => ["frontend", .. Enumerable.Range(1, count - 1).Select(i => $"svc-{i}")]
    };
}
