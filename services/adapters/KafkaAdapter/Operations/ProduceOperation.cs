using System.Diagnostics;
using System.Text.Json;
using Adapters.Contracts;
using Confluent.Kafka;
using KafkaAdapter.Configuration;

namespace KafkaAdapter.Operations;

public sealed class ProduceOperation : IAdapterOperation, IDisposable
{
    private readonly IProducer<Null, string> _producer;
    private readonly string _topic;

    public string OperationName => "kafka-push";

    public ProduceOperation(KafkaAdapterOptions options)
    {
        ProducerConfig config = new() { BootstrapServers = options.Brokers };
        _producer = new ProducerBuilder<Null, string>(config).Build();
        _topic = options.Topic;
    }

    public async Task<ExecuteResponse> ExecuteAsync(ExecuteRequest request, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            string value = JsonSerializer.Serialize(request.Payload);
            Message<Null, string> message = new() { Value = value };

            await _producer.ProduceAsync(_topic, message, cancellationToken);

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

    public void Dispose() => _producer.Dispose();
}
