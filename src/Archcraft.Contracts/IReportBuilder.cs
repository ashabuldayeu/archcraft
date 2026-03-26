using Archcraft.Domain.Entities;

namespace Archcraft.Contracts;

public interface IReportBuilder
{
    RunReport Build(string projectName, IEnumerable<MetricSnapshot> snapshots);
}
