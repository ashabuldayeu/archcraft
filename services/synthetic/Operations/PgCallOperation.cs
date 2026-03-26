using Adapters.Contracts;
using System.Diagnostics;

namespace SynteticApi.Operations;

public sealed class PgCallOperation : IOperation
{
    public const string AdapterName = "pg-adapter";

    private readonly AdapterHttpClient _client;
    private readonly ILogger<PgCallOperation> _logger;

    public string OperationType => "pg-call";

    public PgCallOperation(
        IHttpClientFactory httpClientFactory,
        ILogger<PgCallOperation> logger)
    {
        _client = new AdapterHttpClient(httpClientFactory.CreateClient(AdapterName));
        _logger = logger;
    }

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        ExecuteResponse response = await _client.ExecuteAsync("query", context.CorrelationId, cancellationToken);

        sw.Stop();

        _logger.LogDebug("pg-call adapter response: {Outcome} in {Duration}ms", response.Outcome, response.DurationMs);

        return response.Outcome switch
        {
            AdapterOutcome.NotFound => OperationResult.NotFound(sw.Elapsed),
            AdapterOutcome.Error    => OperationResult.Error(sw.Elapsed),
            _                      => OperationResult.Success(sw.Elapsed)
        };
    }
}
