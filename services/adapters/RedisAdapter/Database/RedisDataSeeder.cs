using Adapters.Contracts;
using StackExchange.Redis;

namespace RedisAdapter.Database;

public sealed class RedisDataSeeder : IDataSeeder
{
    private const string KeyPrefix = "archcraft:";

    private readonly RedisConnectionFactory _factory;
    private readonly ILogger<RedisDataSeeder> _logger;

    public RedisDataSeeder(RedisConnectionFactory factory, ILogger<RedisDataSeeder> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task SeedAsync(int rows, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Seeding {Rows} keys into Redis...", rows);

        IDatabase db = _factory.GetDatabase();

        IBatch batch = db.CreateBatch();
        List<Task> tasks = new(rows);

        for (int i = 0; i < rows; i++)
        {
            string key = KeyPrefix + i;
            string value = Guid.NewGuid().ToString("N");
            tasks.Add(batch.StringSetAsync(key, value));
        }

        batch.Execute();
        await Task.WhenAll(tasks);

        _logger.LogInformation("Seeded {Rows} keys.", rows);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Clearing Redis keys with prefix '{Prefix}'...", KeyPrefix);

        IDatabase db = _factory.GetDatabase();
        IServer server = _factory.GetServer();

        RedisKey[] keys = server.Keys(pattern: KeyPrefix + "*").ToArray();
        if (keys.Length > 0)
            await db.KeyDeleteAsync(keys);

        _logger.LogInformation("Cleared {Count} keys.", keys.Length);
    }
}
