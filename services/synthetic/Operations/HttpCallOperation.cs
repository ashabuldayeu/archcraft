using Adapters.Contracts;
using System.Diagnostics;

namespace SynteticApi.Operations;

public sealed class HttpCallOperation : IOperation
{
    public const string AdapterName = "http-adapter";

    private readonly AdapterHttpClient _client;
    private readonly ILogger<HttpCallOperation> _logger;

    public string OperationType => "http-call";

    public HttpCallOperation(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpCallOperation> logger)
    {
        _client = new AdapterHttpClient(httpClientFactory.CreateClient(AdapterName));
        _logger = logger;
    }

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        ExecuteResponse response = await _client.ExecuteAsync("request", context.CorrelationId, cancellationToken);

        sw.Stop();

        _logger.LogDebug("http-call adapter response: {Outcome} in {Duration}ms", response.Outcome, response.DurationMs);

        return response.Outcome switch
        {
            AdapterOutcome.NotFound => OperationResult.NotFound(sw.Elapsed),
            AdapterOutcome.Error    => OperationResult.Error(sw.Elapsed),
            _                      => OperationResult.Success(sw.Elapsed)
        };
    }
}
