using System.Text.Json;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SynteticApi.Configuration;
using SynteticApi.Endpoints;
using SynteticApi.Observability;
using SynteticApi.Operations;
using SynteticApi.Pipeline;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

SynteticApiOptions options = LoadOptions(builder.Configuration);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<RuntimeConfigStore>(sp =>
{
    RuntimeConfigStore store = new();
    store.Initialize(options.Endpoints);
    return store;
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<AdapterHttpClient>();
builder.Services.AddSingleton<AdapterCallOperation>();

builder.Services.AddSingleton(new SynteticMetrics(options.ServiceName));
builder.Services.AddScoped<PipelineExecutor>();

ResourceBuilder resource = ResourceBuilder.CreateDefault()
    .AddService(options.ServiceName);

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resource)
            .AddMeter(SynteticMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddPrometheusExporter();

        if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            metrics.AddOtlpExporter();
    })
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resource)
            .AddSource(SynteticTracing.ActivitySourceName)
            .AddAspNetCoreInstrumentation();

        if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            tracing.AddOtlpExporter();
    });

WebApplication app = builder.Build();

app.MapPrometheusScrapingEndpoint("/metrics");
app.MapHealthEndpoints();
app.MapConfigEndpoints();
app.MapPipelineEndpoints();

app.Run();

static SynteticApiOptions LoadOptions(IConfiguration configuration)
{
    string? rawJson = configuration["SYNTETIC_CONFIG"];

    if (!string.IsNullOrEmpty(rawJson))
    {
        SynteticApiOptions? fromEnv = JsonSerializer.Deserialize<SynteticApiOptions>(rawJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (fromEnv is not null)
            return fromEnv;
    }

    SynteticApiOptions fromSection = new();
    configuration.GetSection(SynteticApiOptions.SectionName).Bind(fromSection);
    return fromSection;
}
