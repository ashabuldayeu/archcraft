using Archcraft.Domain.ValueObjects;

namespace Archcraft.Domain.Entities;

public sealed record AdapterDefinition
{
    public required string Name { get; init; }
    public required string Image { get; init; }
    public required ServicePort Port { get; init; }
    public required string Technology { get; init; }
    public string? ConnectsTo { get; init; }
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
    public int SeedRows { get; init; }

    /// <summary>
    /// Kafka consumer configuration. When set, the adapter subscribes to the topic
    /// and pushes messages to the paired synthetic service.
    /// </summary>
    public KafkaConsumerConfig? KafkaConsumer { get; init; }

    /// <summary>
    /// Name of the synthetic service replica this adapter is paired with.
    /// Set by the compiler during replica expansion (e.g. "backend-0" for "kafka-adapter-0").
    /// Used to inject KAFKA_CONSUMER_TARGET_URL and to couple kill/restore lifecycle.
    /// </summary>
    public string? PairedReplicaName { get; init; }
}
