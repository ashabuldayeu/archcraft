using System.CommandLine;
using Archcraft.App.UseCases;
using Archcraft.Domain.Entities;
using Archcraft.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Archcraft.Cli.Commands;

public static class RunCommand
{
    public static Command Build(IServiceProvider services)
    {
        Argument<FileInfo> fileArg = new("project-file")
        {
            Description = "Path to project.yaml"
        };

        Command command = new("run", "Run all scenarios in a project")
        {
            fileArg
        };

        command.SetAction(async (ParseResult result, CancellationToken ct) =>
        {
            FileInfo file = result.GetValue(fileArg)!;

            try
            {
                await using AsyncServiceScope scope = services.CreateAsyncScope();
                RunProjectUseCase useCase = scope.ServiceProvider.GetRequiredService<RunProjectUseCase>();
                RunReport report = await useCase.ExecuteAsync(file.FullName, cancellationToken: ct);
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
