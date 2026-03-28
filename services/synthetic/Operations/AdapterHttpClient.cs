using System.Net.Http.Json;
using Adapters.Contracts;

namespace SynteticApi.Operations;

public sealed class AdapterHttpClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AdapterHttpClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ExecuteResponse> ExecuteAsync(
        string adapterUrl,
        string operation,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ExecuteRequest request = new() { Operation = operation };

        using HttpRequestMessage message = new(HttpMethod.Post, new Uri($"{adapterUrl}/execute"))
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        HttpClient client = _httpClientFactory.CreateClient();
        HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        ExecuteResponse? result = await response.Content.ReadFromJsonAsync<ExecuteResponse>(cancellationToken);
        return result ?? ExecuteResponse.Error(0);
    }
}
