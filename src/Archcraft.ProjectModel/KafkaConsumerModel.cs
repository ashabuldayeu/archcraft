using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

/// <summary>
/// Shared YAML model for the consumer: block — used by both Kafka and RabbitMQ adapters.
/// Technology-specific fields are ignored when not applicable.
/// </summary>
public class ConsumerModel
{
    [YamlMember(Alias = "endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [YamlMember(Alias = "consumers")]
    public int Consumers { get; set; } = 1;

    // Kafka-specific
    [YamlMember(Alias = "group_id")]
    public string GroupId { get; set; } = string.Empty;

    [YamlMember(Alias = "partitions")]
    public int Partitions { get; set; } = 1;

    // RabbitMQ-specific
    [YamlMember(Alias = "durable")]
    public bool Durable { get; set; } = true;

    [YamlMember(Alias = "prefetch")]
    public int Prefetch { get; set; } = 1;
}

// Backward-compat alias used by AdapterModel
[Obsolete("Use ConsumerModel")]
public sealed class KafkaConsumerModel : ConsumerModel { }
