using Archcraft.Domain.Entities;

namespace Archcraft.Contracts;

public interface IProjectCompiler
{
    ExecutionPlan Compile(ProjectDefinition project);
}
