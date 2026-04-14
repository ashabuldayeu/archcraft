using Adapters.Contracts;
using Npgsql;

namespace PgAdapter.Database;

public sealed class PgDataSeeder : IDataSeeder
{
    private const int BatchSize = 1000;

    private readonly DbConnectionFactory _factory;
    private readonly ILogger<PgDataSeeder> _logger;

    public PgDataSeeder(DbConnectionFactory factory, ILogger<PgDataSeeder> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task SeedAsync(int rows, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Seeding {Rows} rows into synthetic_items...", rows);

        await using NpgsqlConnection connection = _factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        int seeded = 0;
        while (seeded < rows)
        {
            int batchCount = Math.Min(BatchSize, rows - seeded);

            string values = string.Join(", ",
                Enumerable.Range(seeded, batchCount)
                    .Select(i => $"('item-{i}', '{Guid.NewGuid():N}')"));

            string sql = $"INSERT INTO synthetic_items (name, value) VALUES {values}";

            await using NpgsqlCommand command = new(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            seeded += batchCount;
        }

        _logger.LogInformation("Seeded {Rows} rows.", rows);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Clearing synthetic_items...");

        await using NpgsqlConnection connection = _factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            "TRUNCATE synthetic_items RESTART IDENTITY", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("synthetic_items cleared.");
    }
}
