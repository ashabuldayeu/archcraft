using Adapters.Contracts;
using KafkaAdapter.Configuration;
using KafkaAdapter.Consumer;
using KafkaAdapter.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

KafkaAdapterOptions options = KafkaAdapterOptions.FromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IAdapterOperation, ProduceOperation>();
builder.Services.AddHttpClient();

if (options.IsConsumer)
    builder.Services.AddHostedService<KafkaConsumerWorker>();

WebApplication app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAdapterEndpoints();

app.Run();
