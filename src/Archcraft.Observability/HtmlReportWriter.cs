using System.Text;
using Archcraft.Domain.Entities;

namespace Archcraft.Observability;

public static class HtmlReportWriter
{
    public static async Task WriteAsync(RunReport report, string projectFilePath, CancellationToken cancellationToken = default)
    {
        string projectDir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) ?? Directory.GetCurrentDirectory();
        string resultsDir = Path.Combine(projectDir, "results");
        Directory.CreateDirectory(resultsDir);

        string slug = report.ProjectName.ToLowerInvariant().Replace(' ', '-');
        string timestamp = report.Timestamp.ToString("yyyyMMdd-HHmmss");
        string filePath = Path.Combine(resultsDir, $"{slug}-{timestamp}.html");

        string html = BuildHtml(report);
        await File.WriteAllTextAsync(filePath, html, Encoding.UTF8, cancellationToken);

        Console.WriteLine($"HTML report: {filePath}");
    }

    private static string BuildHtml(RunReport report)
    {
        IReadOnlyList<MetricSnapshot> snapshots = report.Snapshots;
        if (snapshots.Count == 0)
            return "<html><body><p>No scenarios ran.</p></body></html>";

        MetricSnapshot baseline = snapshots[0];

        // ── Summary ────────────────────────────────────────────────────────────
        double bestP50  = snapshots.Min(s => s.P50Ms);
        double worstP99 = snapshots.Max(s => s.P99Ms);
        double maxError = snapshots.Max(s => s.ErrorRate * 100);
        double minSat   = snapshots.Where(s => s.TargetRequests > 0)
                                   .Select(s => s.Saturation * 100)
                                   .DefaultIfEmpty(100)
                                   .Min();

        // ── Chart data ─────────────────────────────────────────────────────────
        double maxP99     = worstP99 == 0 ? 1 : worstP99;
        double maxErrRate = maxError == 0 ? 1 : maxError;

        StringBuilder sb = new();
        sb.Append($$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>{{report.ProjectName}} — Archcraft Report</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #0f1117; color: #e2e8f0; font-size: 14px; }
  h1 { font-size: 22px; font-weight: 600; color: #f8fafc; }
  h2 { font-size: 15px; font-weight: 600; color: #94a3b8; text-transform: uppercase; letter-spacing: .08em; margin-bottom: 12px; }
  .header { padding: 28px 32px 20px; border-bottom: 1px solid #1e293b; display: flex; align-items: baseline; gap: 16px; flex-wrap: wrap; }
  .header .meta { color: #64748b; font-size: 13px; }
  .header a { color: #38bdf8; text-decoration: none; }
  .header a:hover { text-decoration: underline; }
  .section { padding: 24px 32px; border-bottom: 1px solid #1e293b; }
  .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; }
  .card { background: #1e293b; border-radius: 8px; padding: 16px; }
  .card .label { font-size: 12px; color: #64748b; margin-bottom: 6px; }
  .card .value { font-size: 24px; font-weight: 700; color: #f1f5f9; }
  .card .unit  { font-size: 13px; color: #94a3b8; margin-left: 3px; }
  table { width: 100%; border-collapse: collapse; font-size: 13px; }
  th { text-align: right; color: #64748b; font-weight: 500; padding: 8px 10px; border-bottom: 1px solid #1e293b; white-space: nowrap; }
  th:first-child { text-align: left; }
  td { text-align: right; padding: 8px 10px; border-bottom: 1px solid #0f1117; }
  td:first-child { text-align: left; color: #f1f5f9; font-weight: 500; }
  tr:hover td { background: #1e293b44; }
  .badge { display: inline-block; font-size: 10px; padding: 1px 6px; border-radius: 4px; margin-left: 6px; vertical-align: middle; font-weight: 600; letter-spacing: .04em; }
  .badge-blue   { background: #1e40af; color: #bfdbfe; }
  .green  { color: #4ade80; }
  .yellow { color: #facc15; }
  .red    { color: #f87171; }
  .muted  { color: #475569; }
  .charts { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; }
  @media (max-width: 900px) { .charts { grid-template-columns: 1fr; } }
  .chart-wrap { background: #1e293b; border-radius: 8px; padding: 20px; }
  .bar-row { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; font-size: 12px; }
  .bar-label { width: 140px; text-align: right; color: #94a3b8; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; flex-shrink: 0; }
  .bar-track { flex: 1; height: 18px; background: #0f1117; border-radius: 3px; overflow: hidden; }
  .bar-fill  { height: 100%; border-radius: 3px; transition: width .3s; }
  .bar-val   { width: 70px; color: #e2e8f0; flex-shrink: 0; }
</style>
</head>
<body>
<div class="header">
  <h1>{{EscapeHtml(report.ProjectName)}}</h1>
  <span class="meta">{{report.Timestamp:yyyy-MM-dd HH:mm:ss}} UTC</span>
""");

        if (report.GrafanaUrl is not null)
            sb.Append($"  <a class=\"meta\" href=\"{EscapeHtml(report.GrafanaUrl)}\" target=\"_blank\">Open Grafana ↗</a>\n");

        sb.Append($$"""
</div>

<div class="section">
<h2>Summary</h2>
<div class="cards">
  <div class="card"><div class="label">Scenarios</div><div class="value">{{snapshots.Count}}</div></div>
  <div class="card"><div class="label">Best p50</div><div class="value">{{bestP50:F0}}<span class="unit">ms</span></div></div>
  <div class="card"><div class="label">Worst p99</div><div class="value {{P99Color(worstP99)}}">{{worstP99:F0}}<span class="unit">ms</span></div></div>
  <div class="card"><div class="label">Max Error Rate</div><div class="value {{ErrColor(maxError)}}">{{maxError:F2}}<span class="unit">%</span></div></div>
  <div class="card"><div class="label">Min Saturation</div><div class="value {{SatColor(minSat)}}">{{minSat:F1}}<span class="unit">%</span></div></div>
</div>
</div>

<div class="section">
<h2>Scenario Comparison</h2>
<table>
<thead>
  <tr>
    <th>Scenario</th>
    <th>p50 (ms)</th><th>p99 (ms)</th>
    <th>Error %</th>
    <th>Actual</th><th>Target</th><th>Sat %</th>
    <th>Δp50</th><th>Δp99</th><th>ΔErr</th>
  </tr>
</thead>
<tbody>
""");

        foreach (MetricSnapshot s in snapshots)
        {
            bool isBaseline = ReferenceEquals(s, baseline);
            double dp50 = DeltaPct(s.P50Ms, baseline.P50Ms);
            double dp99 = DeltaPct(s.P99Ms, baseline.P99Ms);
            double derr = baseline.ErrorRate > 0
                ? DeltaPct(s.ErrorRate, baseline.ErrorRate)
                : (s.ErrorRate > 0 ? 100 : 0);
            string target = s.TargetRequests > 0 ? $"{s.TargetRequests:N0}" : "—";
            string sat    = s.TargetRequests > 0 ? $"<span class=\"{SatColor(s.Saturation * 100)}\">{s.Saturation * 100:F1}%</span>" : "<span class=\"muted\">—</span>";
            string deltap50 = isBaseline ? "<span class=\"muted\">baseline</span>" : DeltaHtml(dp50);
            string deltap99 = isBaseline ? "<span class=\"muted\">baseline</span>" : DeltaHtml(dp99);
            string deltaerr = isBaseline ? "<span class=\"muted\">baseline</span>" : DeltaHtml(derr);

            sb.Append($"""
  <tr>
    <td>{EscapeHtml(s.ScenarioName)}{(isBaseline ? " <span class=\"badge badge-blue\">baseline</span>" : "")}</td>
    <td class="{P50Color(s.P50Ms)}">{s.P50Ms:F1}</td>
    <td class="{P99Color(s.P99Ms)}">{s.P99Ms:F1}</td>
    <td class="{ErrColor(s.ErrorRate * 100)}">{s.ErrorRate * 100:F2}</td>
    <td>{s.TotalRequests:N0}</td>
    <td>{target}</td>
    <td>{sat}</td>
    <td>{deltap50}</td><td>{deltap99}</td><td>{deltaerr}</td>
  </tr>
""");
        }

        sb.Append("""
</tbody>
</table>
</div>

<div class="section">
<h2>Charts</h2>
<div class="charts">
""");

        sb.Append(BuildBarChart("p99 Latency (ms)", snapshots,
            s => s.P99Ms, maxP99,
            s => BarColor(s.P99Ms, 100, 500),
            s => $"{s.P99Ms:F0} ms"));

        sb.Append(BuildBarChart("Error Rate (%)", snapshots,
            s => s.ErrorRate * 100, maxErrRate == 0 ? 0.1 : maxErrRate,
            s => BarColor(s.ErrorRate * 100, 0.5, 2),
            s => $"{s.ErrorRate * 100:F2}%"));

        sb.Append(BuildBarChart("Saturation (%)", snapshots,
            s => s.TargetRequests > 0 ? s.Saturation * 100 : 0, 100,
            s => SatBarColor(s),
            s => s.TargetRequests > 0 ? $"{s.Saturation * 100:F1}%" : "—"));

        sb.Append(BuildBarChart("p50 Latency (ms)", snapshots,
            s => s.P50Ms, snapshots.Max(s => s.P50Ms) is var m && m == 0 ? 1 : m,
            s => BarColor(s.P50Ms, 50, 200),
            s => $"{s.P50Ms:F0} ms"));

        sb.Append("""
</div>
</div>
</body>
</html>
""");

        return sb.ToString();
    }

    // ── Chart builder ─────────────────────────────────────────────────────────

    private static string BuildBarChart(
        string title,
        IReadOnlyList<MetricSnapshot> snapshots,
        Func<MetricSnapshot, double> value,
        double maxValue,
        Func<MetricSnapshot, string> barColorFn,
        Func<MetricSnapshot, string> labelFn)
    {
        StringBuilder sb = new();
        sb.Append($"<div class=\"chart-wrap\"><h2>{EscapeHtml(title)}</h2><br>");

        foreach (MetricSnapshot s in snapshots)
        {
            double v = value(s);
            double pct = maxValue > 0 ? Math.Min(v / maxValue * 100, 100) : 0;
            string color = barColorFn(s);
            string label = labelFn(s);

            sb.Append($"""
<div class="bar-row">
  <div class="bar-label" title="{EscapeHtml(s.ScenarioName)}">{EscapeHtml(Truncate(s.ScenarioName, 18))}</div>
  <div class="bar-track"><div class="bar-fill" style="width:{pct:F1}%;background:{CssColor(color)}"></div></div>
  <div class="bar-val {color}">{label}</div>
</div>
""");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double DeltaPct(double current, double reference) =>
        reference == 0 ? 0 : (current - reference) / reference * 100;

    private static string DeltaHtml(double delta)
    {
        if (Math.Abs(delta) < 0.5) return "<span class=\"muted\">≈0%</span>";
        string sign = delta > 0 ? "+" : "";
        string cls = delta switch { < -5 => "green", > 50 => "red", > 10 => "yellow", _ => "muted" };
        return $"<span class=\"{cls}\">{sign}{delta:F1}%</span>";
    }

    private static string P50Color(double ms) => ms switch { < 50 => "green", < 200 => "yellow", _ => "red" };
    private static string P99Color(double ms) => ms switch { < 100 => "green", < 500 => "yellow", _ => "red" };
    private static string ErrColor(double pct) => pct switch { < 0.5 => "green", < 2 => "yellow", _ => "red" };
    private static string SatColor(double pct) => pct switch { > 80 => "green", > 50 => "yellow", _ => "red" };
    private static string BarColor(double v, double warn, double danger) =>
        v < warn ? "green" : v < danger ? "yellow" : "red";
    private static string SatBarColor(MetricSnapshot s) =>
        s.TargetRequests == 0 ? "muted" : SatColor(s.Saturation * 100);

    private static string CssColor(string cls) => cls switch
    {
        "green"  => "#4ade80",
        "yellow" => "#facc15",
        "red"    => "#f87171",
        _        => "#475569"
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string EscapeHtml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
