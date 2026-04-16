namespace KafkaAdapter.Configuration;

public sealed class KafkaAdapterOptions
{
    public required string Brokers { get; init; }
    public required string Topic { get; init; }
    public string? ConsumerGroupId { get; init; }
    public string? ConsumerTargetUrl { get; init; }
    public string? ConsumerEndpoint { get; init; }
    public int ConsumerCount { get; init; } = 1;
    public int PartitionCount { get; init; } = 1;

    public bool IsConsumer =>
        ConsumerGroupId is not null &&
        ConsumerTargetUrl is not null &&
        ConsumerEndpoint is not null;

    public static KafkaAdapterOptions FromEnvironment()
    {
        string? brokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS");
        if (string.IsNullOrWhiteSpace(brokers))
            throw new InvalidOperationException("KAFKA_BROKERS environment variable is required.");

        string? topic = Environment.GetEnvironmentVariable("KAFKA_TOPIC");
        if (string.IsNullOrWhiteSpace(topic))
            throw new InvalidOperationException("KAFKA_TOPIC environment variable is required.");

        int consumerCount = int.TryParse(Environment.GetEnvironmentVariable("KAFKA_CONSUMER_COUNT"), out int cc) ? cc : 1;
        int partitionCount = int.TryParse(Environment.GetEnvironmentVariable("KAFKA_PARTITION_COUNT"), out int pc) ? pc : 1;

        return new KafkaAdapterOptions
        {
            Brokers = brokers,
            Topic = topic,
            ConsumerGroupId = Environment.GetEnvironmentVariable("KAFKA_CONSUMER_GROUP_ID"),
            ConsumerTargetUrl = Environment.GetEnvironmentVariable("KAFKA_CONSUMER_TARGET_URL"),
            ConsumerEndpoint = Environment.GetEnvironmentVariable("KAFKA_CONSUMER_ENDPOINT"),
            ConsumerCount = consumerCount,
            PartitionCount = partitionCount
        };
    }
}
