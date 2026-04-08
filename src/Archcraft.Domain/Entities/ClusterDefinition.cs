namespace Archcraft.Domain.Entities;

public sealed record ClusterDefinition
{
    public required int Replicas { get; init; }
    public string ReplicationUser { get; init; } = "replicator";
    public string ReplicationPassword { get; init; } = "replicator_password";
}
