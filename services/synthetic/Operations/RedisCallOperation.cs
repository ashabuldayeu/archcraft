using Adapters.Contracts;
using System.Diagnostics;

namespace SynteticApi.Operations;

public sealed class RedisCallOperation : IOperation
{
    public const string AdapterName = "redis-adapter";

    private readonly AdapterHttpClient _client;
    private readonly ILogger<RedisCallOperation> _logger;

    public string OperationType => "redis-call";

    public RedisCallOperation(
        IHttpClientFactory httpClientFactory,
        ILogger<RedisCallOperation> logger)
    {
        _client = new AdapterHttpClient(httpClientFactory.CreateClient(AdapterName));
        _logger = logger;
    }

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        ExecuteResponse response = await _client.ExecuteAsync("get", context.CorrelationId, cancellationToken);

        sw.Stop();

        _logger.LogDebug("redis-call adapter response: {Outcome} in {Duration}ms", response.Outcome, response.DurationMs);

        return response.Outcome switch
        {
            AdapterOutcome.NotFound => OperationResult.NotFound(sw.Elapsed),
            AdapterOutcome.Error    => OperationResult.Error(sw.Elapsed),
            _                      => OperationResult.Success(sw.Elapsed)
        };
    }
}
