using System.Diagnostics;
using System.Net;
using System.Text;
using Adapters.Contracts;
using HttpAdapter.Http;

namespace HttpAdapter.Operations;

public sealed class RequestOperation : IAdapterOperation
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string ClientName = "target";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RetryPolicy _retry;

    public string OperationName => "http-call";

    public RequestOperation(IHttpClientFactory httpClientFactory, RetryPolicy retry)
    {
        _httpClientFactory = httpClientFactory;
        _retry = retry;
    }

    public async Task<ExecuteResponse> ExecuteAsync(ExecuteRequest request, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            return await _retry.ExecuteAsync(async ct =>
            {
                HttpClient client = _httpClientFactory.CreateClient(ClientName);

                string method = GetString(request, "method");
                string path = GetString(request, "path");
                request.Payload.TryGetValue("body", out object? bodyValue);

                HttpRequestMessage httpRequest = new(new HttpMethod(method), path);

                if (request.CorrelationId is not null)
                    httpRequest.Headers.TryAddWithoutValidation(CorrelationIdHeader, request.CorrelationId);

                if (bodyValue is not null)
                    httpRequest.Content = new StringContent(bodyValue.ToString()!, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await client.SendAsync(httpRequest, ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return ExecuteResponse.NotFound(sw.ElapsedMilliseconds);

                if (response.IsSuccessStatusCode)
                    return ExecuteResponse.Success(sw.ElapsedMilliseconds);

                return new ExecuteResponse
                {
                    Outcome = AdapterOutcome.Error,
                    DurationMs = sw.ElapsedMilliseconds,
                    Data = new Dictionary<string, object?> { ["error"] = $"HTTP {(int)response.StatusCode}" }
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

        return key switch
        {
            "method" => "POST",
            "path"   => "/process",
            _        => throw new ArgumentException($"Payload must contain '{key}'.")
        };
    }
}
