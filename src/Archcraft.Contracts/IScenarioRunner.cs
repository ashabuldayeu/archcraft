using Archcraft.Domain.Entities;

namespace Archcraft.Contracts;

public interface IScenarioRunner
{
    bool CanHandle(ScenarioDefinition scenario);
    Task<MetricSnapshot> RunAsync(ScenarioDefinition scenario, IMetricsCollector collector, CancellationToken cancellationToken = default);
}
