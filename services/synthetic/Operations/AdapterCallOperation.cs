using System.Diagnostics;
using Adapters.Contracts;
using Microsoft.Extensions.Configuration;

namespace SynteticApi.Operations;

public sealed class AdapterCallOperation
{
    private const string EnvVarPrefix = "ADAPTER_OP_";
    private const string EnvVarSuffix = "_URL";

    private readonly Dictionary<string, string> _adapterUrls;
    private readonly AdapterHttpClient _httpClient;
    private readonly ILogger<AdapterCallOperation> _logger;

    public AdapterCallOperation(
        IConfiguration configuration,
        AdapterHttpClient httpClient,
        ILogger<AdapterCallOperation> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _adapterUrls = LoadAdapterUrls(configuration);
    }

    public async Task<OperationResult> ExecuteAsync(
        string operationType,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        string envKey = ToEnvKey(operationType);

        if (!_adapterUrls.TryGetValue(envKey, out string? adapterUrl))
        {
            _logger.LogWarning("No adapter URL configured for operation '{OperationType}' (expected env var '{EnvVar}')",
                operationType, $"{EnvVarPrefix}{envKey}{EnvVarSuffix}");
            return OperationResult.Error(TimeSpan.Zero);
        }

        Stopwatch sw = Stopwatch.StartNew();

        ExecuteResponse response = await _httpClient.ExecuteAsync(
            adapterUrl, operationType, context.CorrelationId, cancellationToken);

        sw.Stop();

        return response.Outcome switch
        {
            AdapterOutcome.NotFound => OperationResult.NotFound(sw.Elapsed),
            AdapterOutcome.Error    => OperationResult.Error(sw.Elapsed),
            _                      => OperationResult.Success(sw.Elapsed)
        };
    }

    private static Dictionary<string, string> LoadAdapterUrls(IConfiguration configuration)
    {
        Dictionary<string, string> urls = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, string?> entry in configuration.AsEnumerable())
        {
            if (entry.Key.StartsWith(EnvVarPrefix, StringComparison.OrdinalIgnoreCase)
                && entry.Key.EndsWith(EnvVarSuffix, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(entry.Value))
            {
                string operationKey = entry.Key[EnvVarPrefix.Length..^EnvVarSuffix.Length];
                urls[operationKey] = entry.Value;
            }
        }

        return urls;
    }

    private static string ToEnvKey(string operationType)
        => operationType.Replace("-", "_").ToUpperInvariant();
}
