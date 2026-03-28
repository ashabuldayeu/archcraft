using RedisAdapter.Configuration;
using StackExchange.Redis;

namespace RedisAdapter.Database;

public sealed class RedisConnectionFactory : IDisposable
{
    private readonly ConnectionMultiplexer _multiplexer;

    public RedisConnectionFactory(RedisAdapterOptions options)
    {
        _multiplexer = ConnectionMultiplexer.Connect(options.ConnectionString);
    }

    public IDatabase GetDatabase() => _multiplexer.GetDatabase();

    public void Dispose() => _multiplexer.Dispose();
}
