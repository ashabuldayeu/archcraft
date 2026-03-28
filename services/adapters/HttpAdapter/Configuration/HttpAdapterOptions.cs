namespace HttpAdapter.Configuration;

public sealed class HttpAdapterOptions
{
    public string TargetUrl { get; private init; } = string.Empty;
    public int RetryCount { get; private init; }
    public int RetryDelayMs { get; private init; }

    public static HttpAdapterOptions FromEnvironment() => new()
    {
        TargetUrl = Environment.GetEnvironmentVariable("HTTP_TARGET_URL")
            ?? throw new InvalidOperationException("HTTP_TARGET_URL environment variable is required."),
        RetryCount = int.TryParse(Environment.GetEnvironmentVariable("HTTP_RETRY_COUNT"), out int retryCount)
            ? retryCount : 0,
        RetryDelayMs = int.TryParse(Environment.GetEnvironmentVariable("HTTP_RETRY_DELAY_MS"), out int retryDelay)
            ? retryDelay : 500
    };
}
