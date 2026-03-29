using System.Diagnostics;
using Adapters.Contracts;
using RedisAdapter.Database;
using StackExchange.Redis;

namespace RedisAdapter.Operations;

public sealed class GetOperation : IAdapterOperation
{
    private const string KeyPrefix = "archcraft:";

    private readonly RedisConnectionFactory _factory;
    private readonly RetryPolicy _retry;

    public string OperationName => "redis-call";

    public GetOperation(RedisConnectionFactory factory, RetryPolicy retry)
    {
        _factory = factory;
        _retry = retry;
    }

    public async Task<ExecuteResponse> ExecuteAsync(ExecuteRequest request, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            return await _retry.ExecuteAsync(async _ =>
            {
                IDatabase db = _factory.GetDatabase();
                string key = KeyPrefix + GetKey(request);

                RedisValue value = await db.StringGetAsync(key);

                if (!value.HasValue)
                    return ExecuteResponse.NotFound(sw.ElapsedMilliseconds);

                return new ExecuteResponse
                {
                    Outcome = AdapterOutcome.Success,
                    DurationMs = sw.ElapsedMilliseconds,
                    Data = new Dictionary<string, object?> { ["value"] = value.ToString() }
                };
            }, cancellationToken);
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

    private static string GetKey(ExecuteRequest request)
    {
        if (request.Payload.TryGetValue("key", out object? value) && value is not null)
            return value.ToString()!;

        return "load-test-key";
    }
}
