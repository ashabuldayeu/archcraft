using Npgsql;
using PgAdapter.Configuration;

namespace PgAdapter.Database;

public sealed class DbConnectionFactory : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public DbConnectionFactory(PgAdapterOptions options)
    {
        NpgsqlDataSourceBuilder builder = new(options.ConnectionString);
        builder.ConnectionStringBuilder.MinPoolSize = options.PoolMinSize;
        builder.ConnectionStringBuilder.MaxPoolSize = options.PoolMaxSize;

        _dataSource = builder.Build();
    }

    public NpgsqlConnection CreateConnection() => _dataSource.CreateConnection();

    public void Dispose() => _dataSource.Dispose();
}
