using Adapters.Contracts;
using RedisAdapter.Configuration;
using RedisAdapter.Database;
using RedisAdapter.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

RedisAdapterOptions options = RedisAdapterOptions.FromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<RedisConnectionFactory>();
builder.Services.AddSingleton<RetryPolicy>();
builder.Services.AddSingleton<IDataSeeder, RedisDataSeeder>();

builder.Services.AddSingleton<IAdapterOperation, GetOperation>();
builder.Services.AddSingleton<IAdapterOperation, SetOperation>();

WebApplication app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAdapterEndpoints();

app.Run();
