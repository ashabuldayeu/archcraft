using System.Net.Http.Json;
using Adapters.Contracts;

namespace SynteticApi.Operations;

public sealed class AdapterHttpClient
{
    private readonly HttpClient _httpClient;

    public AdapterHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ExecuteResponse> ExecuteAsync(
        string operation,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ExecuteRequest request = new() { Operation = operation };

        using HttpRequestMessage message = new(HttpMethod.Post, "/execute")
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        ExecuteResponse? result = await response.Content.ReadFromJsonAsync<ExecuteResponse>(cancellationToken);
        return result ?? ExecuteResponse.Error(0);
    }
}
