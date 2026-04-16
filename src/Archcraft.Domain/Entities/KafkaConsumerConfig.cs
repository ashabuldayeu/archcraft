namespace Archcraft.Domain.Entities;

/// <summary>
/// Consumer configuration for a Kafka adapter.
/// When present, the adapter subscribes to the configured topic
/// and pushes each received message to the paired synthetic service endpoint.
/// </summary>
public sealed record KafkaConsumerConfig
{
    public required string GroupId { get; init; }
    public required string Endpoint { get; init; }
    public int ConsumerCount { get; init; } = 1;
    public int PartitionCount { get; init; } = 1;
}
