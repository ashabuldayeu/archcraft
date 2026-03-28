namespace PgAdapter.Configuration;

public sealed class PgAdapterOptions
{
    public required string ConnectionString { get; init; }
    public int PoolMinSize { get; init; } = 1;
    public int PoolMaxSize { get; init; } = 10;
    public int RetryCount { get; init; } = 0;
    public int RetryDelayMs { get; init; } = 500;

    public static PgAdapterOptions FromEnvironment()
    {
        string? connectionString = Environment.GetEnvironmentVariable("PG_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("PG_CONNECTION_STRING environment variable is required.");

        return new PgAdapterOptions
        {
            ConnectionString = connectionString,
            PoolMinSize = ParseInt("PG_POOL_MIN_SIZE", 1),
            PoolMaxSize = ParseInt("PG_POOL_MAX_SIZE", 10),
            RetryCount = ParseInt("PG_RETRY_COUNT", 0),
            RetryDelayMs = ParseInt("PG_RETRY_DELAY_MS", 500)
        };
    }

    private static int ParseInt(string envVar, int defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(envVar);
        return int.TryParse(value, out int result) ? result : defaultValue;
    }
}
