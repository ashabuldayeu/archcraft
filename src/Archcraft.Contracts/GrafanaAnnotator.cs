using System.Net.Http.Json;

namespace Archcraft.Contracts;

/// <summary>
/// Posts annotations to the Grafana HTTP API so scenario events (load start/stop,
/// latency injection, kill, restore) appear as vertical markers on all dashboards.
/// </summary>
public sealed class GrafanaAnnotator : IAsyncDisposable
{
    private readonly HttpClient _http;

    public GrafanaAnnotator(string grafanaBaseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(grafanaBaseUrl) };
        // Grafana 9+ requires authentication even for anonymous-admin instances
        // when writing via the HTTP API. Basic auth with the default admin credentials.
        string credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("admin:admin"));
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    /// <summary>
    /// Posts a point annotation (no duration) — e.g. "load started", "replica restored".
    /// </summary>
    public Task AnnotateAsync(string text, string[] tags, CancellationToken cancellationToken = default) =>
        PostAsync(text, tags, DateTimeOffset.UtcNow, null, cancellationToken);

    /// <summary>
    /// Posts a region annotation spanning a known duration — e.g. "latency injected for 120s".
    /// </summary>
    public Task AnnotateRegionAsync(string text, string[] tags, TimeSpan duration, CancellationToken cancellationToken = default) =>
        PostAsync(text, tags, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow + duration, cancellationToken);

    private async Task PostAsync(
        string text,
        string[] tags,
        DateTimeOffset time,
        DateTimeOffset? timeEnd,
        CancellationToken cancellationToken)
    {
        object body = timeEnd.HasValue
            ? new { text, tags, time = time.ToUnixTimeMilliseconds(), timeEnd = timeEnd.Value.ToUnixTimeMilliseconds() }
            : new { text, tags, time = time.ToUnixTimeMilliseconds() };

        try
        {
            await _http.PostAsJsonAsync("/api/annotations", body, cancellationToken);
        }
        catch
        {
            // Annotation failures must never interrupt scenario execution.
        }
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
