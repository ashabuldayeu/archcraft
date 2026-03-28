using Adapters.Contracts;
using HttpAdapter.Configuration;
using HttpAdapter.Http;
using HttpAdapter.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

HttpAdapterOptions options = HttpAdapterOptions.FromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<RetryPolicy>();

builder.Services.AddHttpClient("target", client =>
    client.BaseAddress = new Uri(options.TargetUrl));

builder.Services.AddSingleton<IAdapterOperation, RequestOperation>();

WebApplication app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAdapterEndpoints();

app.Run();
