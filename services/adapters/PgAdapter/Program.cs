using Adapters.Contracts;
using PgAdapter.Configuration;
using PgAdapter.Database;
using PgAdapter.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

PgAdapterOptions options = PgAdapterOptions.FromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<RetryPolicy>();
builder.Services.AddSingleton<PgSchemaInitializer>();
builder.Services.AddSingleton<IDataSeeder, PgDataSeeder>();

builder.Services.AddSingleton<IAdapterOperation, QueryOperation>();
builder.Services.AddSingleton<IAdapterOperation, InsertOperation>();
builder.Services.AddSingleton<IAdapterOperation, UpdateOperation>();

WebApplication app = builder.Build();

PgSchemaInitializer schemaInitializer = app.Services.GetRequiredService<PgSchemaInitializer>();
await schemaInitializer.InitializeAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAdapterEndpoints();

app.Run();
