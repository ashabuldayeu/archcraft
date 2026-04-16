using Confluent.Kafka;
using Confluent.Kafka.Admin;
using KafkaAdapter.Configuration;

namespace KafkaAdapter.Consumer;

public sealed class KafkaConsumerWorker : BackgroundService
{
    private readonly KafkaAdapterOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KafkaConsumerWorker> _logger;

    public KafkaConsumerWorker(
        KafkaAdapterOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<KafkaConsumerWorker> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConsumer)
        {
            _logger.LogInformation("Consumer not configured — running in producer-only mode.");
            return;
        }

        await EnsureTopicExistsAsync(stoppingToken);

        _logger.LogInformation(
            "Starting {Count} Kafka consumer(s). Topic={Topic}, Group={Group}, Target={Target}{Endpoint}",
            _options.ConsumerCount, _options.Topic, _options.ConsumerGroupId,
            _options.ConsumerTargetUrl, _options.ConsumerEndpoint);

        Task[] loops = Enumerable
            .Range(0, _options.ConsumerCount)
            .Select(i => RunConsumerLoopAsync(i, stoppingToken))
            .ToArray();

        await Task.WhenAll(loops);
    }

    private async Task RunConsumerLoopAsync(int index, CancellationToken stoppingToken)
    {
        ConsumerConfig config = new()
        {
            BootstrapServers = _options.Brokers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using IConsumer<Ignore, string> consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        consumer.Subscribe(_options.Topic);

        _logger.LogInformation("Consumer[{Index}] subscribed to topic '{Topic}'.", index, _options.Topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ConsumeResult<Ignore, string> result = consumer.Consume(stoppingToken);
                await ForwardMessageAsync(result.Message.Value, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Consumer[{Index}] error consuming Kafka message.", index);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        consumer.Close();
        _logger.LogInformation("Consumer[{Index}] stopped.", index);
    }

    private async Task EnsureTopicExistsAsync(CancellationToken cancellationToken)
    {
        using IAdminClient admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = _options.Brokers }).Build();

        try
        {
            await admin.CreateTopicsAsync([
                new TopicSpecification
                {
                    Name = _options.Topic,
                    NumPartitions = _options.PartitionCount,
                    ReplicationFactor = 1
                }
            ]);
            _logger.LogInformation(
                "Topic '{Topic}' created with {Partitions} partition(s).",
                _options.Topic, _options.PartitionCount);
        }
        catch (CreateTopicsException ex)
            when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            await EnsurePartitionCountAsync(admin, cancellationToken);
        }
    }

    private async Task EnsurePartitionCountAsync(IAdminClient admin, CancellationToken cancellationToken)
    {
        DescribeTopicsResult result = await admin.DescribeTopicsAsync(
            TopicCollection.OfTopicNames([_options.Topic]));

        TopicDescription topic = result.TopicDescriptions.Single();
        int current = topic.Partitions.Count;

        if (current >= _options.PartitionCount)
        {
            _logger.LogInformation(
                "Topic '{Topic}' already exists with {Current} partition(s).",
                _options.Topic, current);
            return;
        }

        _logger.LogInformation(
            "Topic '{Topic}' has {Current} partition(s), increasing to {Target}.",
            _options.Topic, current, _options.PartitionCount);

        await admin.CreatePartitionsAsync([
            new PartitionsSpecification
            {
                Topic = _options.Topic,
                IncreaseTo = _options.PartitionCount
            }
        ]);

        _logger.LogInformation(
            "Topic '{Topic}' partition count increased to {Target}.",
            _options.Topic, _options.PartitionCount);
    }

    private async Task ForwardMessageAsync(string payload, CancellationToken cancellationToken)
    {
        string url = _options.ConsumerTargetUrl! + _options.ConsumerEndpoint!;

        try
        {
            HttpClient client = _httpClientFactory.CreateClient();
            StringContent content = new(payload, System.Text.Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Forward to {Url} returned {Status}.", url, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to forward message to {Url}.", url);
        }
    }
}
