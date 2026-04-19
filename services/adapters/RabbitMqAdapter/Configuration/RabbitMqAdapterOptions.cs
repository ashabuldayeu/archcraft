namespace RabbitMqAdapter.Configuration;

public sealed class RabbitMqAdapterOptions
{
    public required string AmqpUrl { get; init; }
    public required string Queue { get; init; }
    public string? ConsumerTargetUrl { get; init; }
    public string? ConsumerEndpoint { get; init; }
    public int ConsumerCount { get; init; } = 1;
    public bool Durable { get; init; } = true;
    public int Prefetch { get; init; } = 1;

    public bool IsConsumer =>
        ConsumerTargetUrl is not null && ConsumerEndpoint is not null;

    public static RabbitMqAdapterOptions FromEnvironment()
    {
        string? url = Environment.GetEnvironmentVariable("RABBITMQ_URL");
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("RABBITMQ_URL environment variable is required.");

        string? queue = Environment.GetEnvironmentVariable("RABBITMQ_QUEUE");
        if (string.IsNullOrWhiteSpace(queue))
            throw new InvalidOperationException("RABBITMQ_QUEUE environment variable is required.");

        int consumerCount = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_CONSUMER_COUNT"), out int cc) ? cc : 1;
        bool durable = !string.Equals(Environment.GetEnvironmentVariable("RABBITMQ_DURABLE"), "false", StringComparison.OrdinalIgnoreCase);
        int prefetch = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PREFETCH"), out int pf) ? pf : 1;

        return new RabbitMqAdapterOptions
        {
            AmqpUrl = url,
            Queue = queue,
            ConsumerTargetUrl = Environment.GetEnvironmentVariable("RABBITMQ_CONSUMER_TARGET_URL"),
            ConsumerEndpoint = Environment.GetEnvironmentVariable("RABBITMQ_CONSUMER_ENDPOINT"),
            ConsumerCount = consumerCount,
            Durable = durable,
            Prefetch = prefetch
        };
    }
}
