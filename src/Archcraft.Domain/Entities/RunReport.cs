namespace Archcraft.Domain.Entities;

public sealed record RunReport
{
    public required Guid RunId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string ProjectName { get; init; }
    public required IReadOnlyList<MetricSnapshot> Snapshots { get; init; }
}
