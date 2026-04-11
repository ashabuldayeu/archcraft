using System.CommandLine;
using Archcraft.App.UseCases;
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

        Command command = new("run", "Start an interactive session for a project")
        {
            fileArg
        };

        command.SetAction(async (ParseResult result, CancellationToken ct) =>
        {
            FileInfo file = result.GetValue(fileArg)!;

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

            try
            {
                await using AsyncServiceScope scope = services.CreateAsyncScope();
                InteractiveSessionUseCase useCase =
                    scope.ServiceProvider.GetRequiredService<InteractiveSessionUseCase>();
                await useCase.RunAsync(file.FullName, cts.Token);
                return 0;
            }
            catch (OperationCanceledException)
            {
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
