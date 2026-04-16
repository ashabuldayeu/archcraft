using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class KafkaConsumerModel
{
    [YamlMember(Alias = "group_id")]
    public string GroupId { get; set; } = string.Empty;

    [YamlMember(Alias = "endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [YamlMember(Alias = "consumers")]
    public int Consumers { get; set; } = 1;

    [YamlMember(Alias = "partitions")]
    public int Partitions { get; set; } = 1;
}
