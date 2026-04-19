using Adapters.Contracts;
using RabbitMqAdapter.Configuration;
using RabbitMqAdapter.Consumer;
using RabbitMqAdapter.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

RabbitMqAdapterOptions options = RabbitMqAdapterOptions.FromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IAdapterOperation, PublishOperation>();
builder.Services.AddHttpClient();

if (options.IsConsumer)
    builder.Services.AddHostedService<RabbitMqConsumerWorker>();

WebApplication app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAdapterEndpoints();

app.Run();
