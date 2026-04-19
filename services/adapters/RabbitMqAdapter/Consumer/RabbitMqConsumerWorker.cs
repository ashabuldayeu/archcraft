using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMqAdapter.Configuration;

namespace RabbitMqAdapter.Consumer;

public sealed class RabbitMqConsumerWorker : BackgroundService
{
    private readonly RabbitMqAdapterOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RabbitMqConsumerWorker> _logger;

    public RabbitMqConsumerWorker(
        RabbitMqAdapterOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<RabbitMqConsumerWorker> logger)
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

        ConnectionFactory factory = new()
        {
            Uri = new Uri(_options.AmqpUrl),
            AutomaticRecoveryEnabled = true,
            DispatchConsumersAsync = true
        };

        using IConnection connection = factory.CreateConnection();

        _logger.LogInformation(
            "Starting {Count} RabbitMQ consumer(s). Queue={Queue}, Target={Target}{Endpoint}",
            _options.ConsumerCount, _options.Queue, _options.ConsumerTargetUrl, _options.ConsumerEndpoint);

        Task[] loops = Enumerable
            .Range(0, _options.ConsumerCount)
            .Select(i => RunConsumerLoopAsync(connection, i, stoppingToken))
            .ToArray();

        await Task.WhenAll(loops);
    }

    private async Task RunConsumerLoopAsync(IConnection connection, int index, CancellationToken stoppingToken)
    {
        using IModel channel = connection.CreateModel();

        channel.QueueDeclare(_options.Queue, _options.Durable, exclusive: false, autoDelete: false, arguments: null);
        channel.BasicQos(0, (ushort)_options.Prefetch, false);

        AsyncEventingBasicConsumer consumer = new(channel);
        consumer.Received += async (_, ea) =>
        {
            string payload = Encoding.UTF8.GetString(ea.Body.ToArray());
            bool success = await ForwardMessageAsync(payload, stoppingToken);

            if (success)
                channel.BasicAck(ea.DeliveryTag, false);
            else
                channel.BasicNack(ea.DeliveryTag, false, requeue: true);
        };

        string tag = channel.BasicConsume(_options.Queue, autoAck: false, consumer: consumer);
        _logger.LogInformation("Consumer[{Index}] started on queue '{Queue}' (tag={Tag}).", index, _options.Queue, tag);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CancellationTokenRegistration _ = stoppingToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        channel.BasicCancel(tag);
        _logger.LogInformation("Consumer[{Index}] stopped.", index);
    }

    private async Task<bool> ForwardMessageAsync(string payload, CancellationToken cancellationToken)
    {
        string url = _options.ConsumerTargetUrl! + _options.ConsumerEndpoint!;

        try
        {
            HttpClient client = _httpClientFactory.CreateClient();
            StringContent content = new(payload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Forward to {Url} returned {Status}.", url, (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to forward message to {Url}.", url);
            return false;
        }
    }
}
