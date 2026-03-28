namespace RedisAdapter.Configuration;

public sealed class RedisAdapterOptions
{
    public string ConnectionString { get; private init; } = string.Empty;
    public int RetryCount { get; private init; }
    public int RetryDelayMs { get; private init; }

    public static RedisAdapterOptions FromEnvironment() => new()
    {
        ConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
            ?? throw new InvalidOperationException("REDIS_CONNECTION_STRING environment variable is required."),
        RetryCount = int.TryParse(Environment.GetEnvironmentVariable("REDIS_RETRY_COUNT"), out int retryCount)
            ? retryCount : 0,
        RetryDelayMs = int.TryParse(Environment.GetEnvironmentVariable("REDIS_RETRY_DELAY_MS"), out int retryDelay)
            ? retryDelay : 500
    };
}
