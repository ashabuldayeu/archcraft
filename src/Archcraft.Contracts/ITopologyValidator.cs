using Archcraft.Domain.Entities;

namespace Archcraft.Contracts;

public interface ITopologyValidator
{
    ValidationResult Validate(
        ServiceTopology topology,
        IReadOnlyList<ServiceDefinition> services,
        IReadOnlyList<AdapterDefinition> adapters);
}
