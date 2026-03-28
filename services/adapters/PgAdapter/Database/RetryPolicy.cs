using PgAdapter.Configuration;

namespace PgAdapter.Database;

public sealed class RetryPolicy
{
    private readonly int _retryCount;
    private readonly TimeSpan _retryDelay;

    public RetryPolicy(PgAdapterOptions options)
    {
        _retryCount = options.RetryCount;
        _retryDelay = TimeSpan.FromMilliseconds(options.RetryDelayMs);
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        int attempt = 0;

        while (true)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (Exception) when (attempt < _retryCount)
            {
                attempt++;
                await Task.Delay(_retryDelay, cancellationToken);
            }
        }
    }
}
