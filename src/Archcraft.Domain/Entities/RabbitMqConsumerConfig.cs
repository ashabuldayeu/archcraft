namespace Archcraft.Domain.Entities;

public sealed record RabbitMqConsumerConfig
{
    public required string Endpoint { get; init; }
    public int ConsumerCount { get; init; } = 1;
    public bool Durable { get; init; } = true;
    public int Prefetch { get; init; } = 1;
}
