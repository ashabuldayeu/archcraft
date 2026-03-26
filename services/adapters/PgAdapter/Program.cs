using Adapters.Contracts;
using PgAdapter.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IAdapterOperation, QueryOperation>();
builder.Services.AddSingleton<IAdapterOperation, InsertOperation>();
builder.Services.AddSingleton<IAdapterOperation, UpdateOperation>();

WebApplication app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAdapterEndpoints();

app.Run();
