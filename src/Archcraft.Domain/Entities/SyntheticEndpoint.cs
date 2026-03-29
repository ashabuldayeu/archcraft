namespace Archcraft.Domain.Entities;

public sealed record SyntheticEndpoint
{
    public required string Alias { get; init; }
    public IReadOnlyList<PipelineStep> Pipeline { get; init; } = [];
}

public sealed record PipelineStep
{
    public required string Operation { get; init; }
    public double NotFoundRate { get; init; }
    public IReadOnlyList<PipelineStep> Fallback { get; init; } = [];
    public IReadOnlyList<PipelineStep> Children { get; init; } = [];
}
