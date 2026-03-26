using Adapters.Contracts;
using HttpAdapter.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IAdapterOperation, RequestOperation>();

WebApplication app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAdapterEndpoints();

app.Run();
