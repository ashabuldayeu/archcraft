using Npgsql;

namespace PgAdapter.Database;

public sealed class PgSchemaInitializer
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS synthetic_items (
            id         SERIAL PRIMARY KEY,
            name       TEXT NOT NULL,
            value      TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    private readonly DbConnectionFactory _factory;
    private readonly ILogger<PgSchemaInitializer> _logger;

    public PgSchemaInitializer(DbConnectionFactory factory, ILogger<PgSchemaInitializer> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = _factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(CreateTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Schema initialized: synthetic_items table ready.");
    }
}
