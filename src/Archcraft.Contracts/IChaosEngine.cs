using Archcraft.Domain.Entities;
using Archcraft.Domain.Enums;

namespace Archcraft.Contracts;

/// <summary>Stub — chaos injection on a specific connection. Not implemented in MVP.</summary>
public interface IChaosEngine
{
    Task ApplyAsync(ChaosActionType action, ConnectionDefinition connection, CancellationToken cancellationToken = default);
}
