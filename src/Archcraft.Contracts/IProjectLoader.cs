using Archcraft.Domain.Entities;

namespace Archcraft.Contracts;

public interface IProjectLoader
{
    Task<ProjectDefinition> LoadAsync(string filePath, CancellationToken cancellationToken = default);
}
