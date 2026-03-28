using System.Diagnostics;
using Adapters.Contracts;
using Npgsql;
using PgAdapter.Database;

namespace PgAdapter.Operations;

public sealed class UpdateOperation : IAdapterOperation
{
    private const string Sql = "UPDATE synthetic_items SET name = @name, value = @value WHERE id = @id";

    private readonly DbConnectionFactory _factory;
    private readonly RetryPolicy _retry;

    public string OperationName => "update";

    public UpdateOperation(DbConnectionFactory factory, RetryPolicy retry)
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
                command.Parameters.AddWithValue("name", GetString(request, "name"));
                command.Parameters.AddWithValue("value", GetString(request, "value"));

                int affected = await command.ExecuteNonQueryAsync(ct);

                return affected == 0
                    ? ExecuteResponse.NotFound(sw.ElapsedMilliseconds)
                    : ExecuteResponse.Success(sw.ElapsedMilliseconds);
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

    private static string GetString(ExecuteRequest request, string key)
    {
        if (request.Payload.TryGetValue(key, out object? value) && value is not null)
            return value.ToString()!;

        throw new ArgumentException($"Payload must contain '{key}'.");
    }
}
