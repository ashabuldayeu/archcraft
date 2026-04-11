using Archcraft.Domain.Entities;

namespace Archcraft.Observability;

public static class ConsoleReportRenderer
{
    public static void Render(RunReport report)
    {
        Console.WriteLine();
        Console.WriteLine($"╔══ Run Report: {report.ProjectName} ══════════════════════════════════════╗");
        Console.WriteLine($"║  Run ID:   {report.RunId}");
        Console.WriteLine($"║  Time:     {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  {"Scenario",-25} {"p50 (ms)",10} {"p99 (ms)",10} {"Error %",10} {"Requests",10}  ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");

        foreach (MetricSnapshot snapshot in report.Snapshots)
        {
            Console.WriteLine(
                $"║  {snapshot.ScenarioName,-25} {snapshot.P50Ms,10:F1} {snapshot.P99Ms,10:F1} " +
                $"{snapshot.ErrorRate * 100,10:F2} {snapshot.TotalRequests,10}  ║");
        }

        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");

        if (report.GrafanaUrl is not null)
            Console.WriteLine($"  Grafana:  {report.GrafanaUrl}  (admin / admin)");

        Console.WriteLine();
    }
}
