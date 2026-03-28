using System.Diagnostics;
using Adapters.Contracts;
using RedisAdapter.Database;
using StackExchange.Redis;

namespace RedisAdapter.Operations;

public sealed class SetOperation : IAdapterOperation
{
    private const string KeyPrefix = "archcraft:";

    private readonly RedisConnectionFactory _factory;
    private readonly RetryPolicy _retry;

    public string OperationName => "set";

    public SetOperation(RedisConnectionFactory factory, RetryPolicy retry)
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
                string value = GetValue(request);

                await db.StringSetAsync(key, value);

                return ExecuteResponse.Success(sw.ElapsedMilliseconds);
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

        throw new ArgumentException("Payload must contain 'key'.");
    }

    private static string GetValue(ExecuteRequest request)
    {
        if (request.Payload.TryGetValue("value", out object? value) && value is not null)
            return value.ToString()!;

        throw new ArgumentException("Payload must contain 'value'.");
    }
}
