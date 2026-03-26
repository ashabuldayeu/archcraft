using Adapters.Contracts;
using RedisAdapter.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IAdapterOperation, GetOperation>();
builder.Services.AddSingleton<IAdapterOperation, SetOperation>();

WebApplication app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAdapterEndpoints();

app.Run();
