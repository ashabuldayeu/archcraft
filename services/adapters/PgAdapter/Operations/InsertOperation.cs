using System.Diagnostics;
using Adapters.Contracts;
using Npgsql;
using PgAdapter.Database;

namespace PgAdapter.Operations;

public sealed class InsertOperation : IAdapterOperation
{
    private const string Sql = "INSERT INTO synthetic_items (name, value) VALUES (@name, @value) RETURNING id";

    private readonly DbConnectionFactory _factory;
    private readonly RetryPolicy _retry;

    public string OperationName => "insert";

    public InsertOperation(DbConnectionFactory factory, RetryPolicy retry)
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
                command.Parameters.AddWithValue("name", GetString(request, "name"));
                command.Parameters.AddWithValue("value", GetString(request, "value"));

                object? result = await command.ExecuteScalarAsync(ct);

                return new ExecuteResponse
                {
                    Outcome = AdapterOutcome.Success,
                    DurationMs = sw.ElapsedMilliseconds,
                    Data = new Dictionary<string, object?> { ["id"] = result }
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

    private static string GetString(ExecuteRequest request, string key)
    {
        if (request.Payload.TryGetValue(key, out object? value) && value is not null)
            return value.ToString()!;

        throw new ArgumentException($"Payload must contain '{key}'.");
    }
}
