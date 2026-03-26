using System.Text.Json;
using System.Text.Json.Serialization;
using Archcraft.Domain.Entities;

namespace Archcraft.Observability;

public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task WriteAsync(RunReport report, string projectFilePath, CancellationToken cancellationToken = default)
    {
        string projectDir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) ?? Directory.GetCurrentDirectory();
        string resultsDir = Path.Combine(projectDir, "results");
        Directory.CreateDirectory(resultsDir);

        string slug = report.ProjectName.ToLowerInvariant().Replace(' ', '-');
        string timestamp = report.Timestamp.ToString("yyyyMMdd-HHmmss");
        string filePath = Path.Combine(resultsDir, $"{slug}-{timestamp}.json");

        string json = JsonSerializer.Serialize(report, Options);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        Console.WriteLine($"Report saved: {filePath}");
    }
}
