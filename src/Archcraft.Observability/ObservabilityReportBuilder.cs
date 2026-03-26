using Archcraft.Contracts;
using Archcraft.Domain.Entities;

namespace Archcraft.Observability;

public sealed class ObservabilityReportBuilder : IReportBuilder
{
    public RunReport Build(string projectName, IEnumerable<MetricSnapshot> snapshots) =>
        new()
        {
            RunId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ProjectName = projectName,
            Snapshots = snapshots.ToList()
        };
}
