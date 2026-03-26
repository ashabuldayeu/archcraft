using System.CommandLine;
using Archcraft.App.UseCases;
using Archcraft.Domain.Entities;
using Archcraft.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Archcraft.Cli.Commands;

public static class ScenarioCommand
{
    public static Command Build(IServiceProvider services)
    {
        Command scenarioCommand = new("scenario", "Scenario management commands");
        scenarioCommand.Subcommands.Add(BuildRunSubcommand(services));
        return scenarioCommand;
    }

    private static Command BuildRunSubcommand(IServiceProvider services)
    {
        Argument<FileInfo> fileArg = new("project-file")
        {
            Description = "Path to project.yaml"
        };

        Option<string> scenarioOption = new("--scenario", "-s")
        {
            Description = "Name of the scenario to run",
            Required = true
        };

        Command command = new("run", "Run a single named scenario")
        {
            fileArg,
            scenarioOption
        };

        command.SetAction(async (ParseResult result, CancellationToken ct) =>
        {
            FileInfo file = result.GetValue(fileArg)!;
            string scenario = result.GetValue(scenarioOption)!;
            RunProjectUseCase useCase = services.GetRequiredService<RunProjectUseCase>();

            try
            {
                RunReport report = await useCase.ExecuteAsync(file.FullName, scenario, ct);
                ConsoleReportRenderer.Render(report);
                await JsonReportWriter.WriteAsync(report, file.FullName, CancellationToken.None);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}
