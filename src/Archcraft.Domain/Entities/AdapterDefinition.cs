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

    public KafkaConsumerConfig? KafkaConsumer { get; init; }
    public RabbitMqConsumerConfig? RabbitMqConsumer { get; init; }

    /// <summary>
    /// Name of the synthetic service replica this adapter is paired with (set during replica expansion).
    /// Couples kill/restore lifecycle between the adapter and its synthetic service replica.
    /// </summary>
    public string? PairedReplicaName { get; init; }
}
