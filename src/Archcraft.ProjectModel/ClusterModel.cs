using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class ClusterModel
{
    [YamlMember(Alias = "replicas")]
    public int Replicas { get; set; } = 1;

    [YamlMember(Alias = "replication_user")]
    public string? ReplicationUser { get; set; }

    [YamlMember(Alias = "replication_password")]
    public string? ReplicationPassword { get; set; }
}
