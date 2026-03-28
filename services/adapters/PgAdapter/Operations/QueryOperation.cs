using System.Diagnostics;
using Adapters.Contracts;
using Npgsql;
using PgAdapter.Database;

namespace PgAdapter.Operations;

public sealed class QueryOperation : IAdapterOperation
{
    private const string Sql = "SELECT id, name, value, created_at FROM synthetic_items WHERE id = @id";

    private readonly DbConnectionFactory _factory;
    private readonly RetryPolicy _retry;

    public string OperationName => "query";

    public QueryOperation(DbConnectionFactory factory, RetryPolicy retry)
    {
        _factory = factory;
        _retry = retry;
    }

    public async Task<ExecuteResponse> ExecuteAsync(ExecuteRequest request, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            return await _retry.ExecuteAsync(async ct =>
            {
                await using NpgsqlConnection connection = _factory.CreateConnection();
                await connection.OpenAsync(ct);

                await using NpgsqlCommand command = new(Sql, connection);
                command.Parameters.AddWithValue("id", GetId(request));

                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);

                if (!await reader.ReadAsync(ct))
                    return ExecuteResponse.NotFound(sw.ElapsedMilliseconds);

                Dictionary<string, object?> data = new()
                {
                    ["id"]         = reader.GetInt32(0),
                    ["name"]       = reader.GetString(1),
                    ["value"]      = reader.GetString(2),
                    ["created_at"] = reader.GetDateTime(3).ToString("O")
                };

                return new ExecuteResponse
                {
                    Outcome = AdapterOutcome.Success,
                    DurationMs = sw.ElapsedMilliseconds,
                    Data = data
                };
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

    private static int GetId(ExecuteRequest request)
    {
        if (request.Payload.TryGetValue("id", out object? value) && value is not null)
            return Convert.ToInt32(value);

        throw new ArgumentException("Payload must contain 'id'.");
    }
}
