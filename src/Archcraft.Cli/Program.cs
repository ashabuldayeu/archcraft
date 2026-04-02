using System.CommandLine;
using Archcraft.App;
using Archcraft.Cli.Commands;
using Archcraft.Contracts;
using Archcraft.Execution.Docker;
using Archcraft.Observability;
using Archcraft.ProjectCompiler;
using Archcraft.Scenarios;
using Archcraft.Serialization.Yaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

ServiceCollection services = new();

services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddHttpClient();

// Ports → Adapters
services.AddSingleton<IProjectLoader, YamlProjectLoader>();
services.AddSingleton<ITopologyValidator, TopologyValidator>();
services.AddSingleton<IProjectCompiler, ArchcraftProjectCompiler>();
services.AddScoped<IEnvironmentRunner, DockerEnvironmentRunner>();
services.AddTransient<IScenarioRunner, HttpScenarioRunner>();
services.AddTransient<TimelineScenarioRunner>();
services.AddTransient<IMetricsCollector, InMemoryMetricsCollector>();
services.AddTransient<IReportBuilder, ObservabilityReportBuilder>();
services.AddTransient<DashboardGenerator>();

services.AddArchcraftApp();

ServiceProvider provider = services.BuildServiceProvider();

RootCommand root = new("Archcraft — declarative backend sandbox & scenario runner")
{
    RunCommand.Build(provider),
    ValidateCommand.Build(provider),
    ScenarioCommand.Build(provider)
};

return await root.Parse(args).InvokeAsync();
