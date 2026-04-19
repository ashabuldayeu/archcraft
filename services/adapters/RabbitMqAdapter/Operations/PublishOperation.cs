using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Adapters.Contracts;
using RabbitMQ.Client;
using RabbitMqAdapter.Configuration;

namespace RabbitMqAdapter.Operations;

public sealed class PublishOperation : IAdapterOperation, IDisposable
{
    private readonly RabbitMqAdapterOptions _options;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string OperationName => "rabbitmq-push";

    public PublishOperation(RabbitMqAdapterOptions options)
    {
        _options = options;
    }

    public async Task<ExecuteResponse> ExecuteAsync(ExecuteRequest request, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                EnsureChannel();
                string json = JsonSerializer.Serialize(request.Payload);
                byte[] body = Encoding.UTF8.GetBytes(json);
                IBasicProperties props = _channel!.CreateBasicProperties();
                props.Persistent = _options.Durable;
                _channel.BasicPublish("", _options.Queue, props, body);
            }
            finally
            {
                _lock.Release();
            }

            return new ExecuteResponse
            {
                Outcome = AdapterOutcome.Success,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new ExecuteResponse
            {
                Outcome = AdapterOutcome.Error,
                DurationMs = sw.ElapsedMilliseconds,
                Data = new Dictionary<string, object?> { ["error"] = ex.Message }
            };
        }
    }

    private void EnsureChannel()
    {
        if (_channel is { IsOpen: true }) return;

        _connection?.Dispose();
        ConnectionFactory factory = new()
        {
            Uri = new Uri(_options.AmqpUrl),
            AutomaticRecoveryEnabled = true,
            DispatchConsumersAsync = true
        };
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(_options.Queue, _options.Durable, exclusive: false, autoDelete: false, arguments: null);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        _lock.Dispose();
    }
}
