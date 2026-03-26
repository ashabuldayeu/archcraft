using System.CommandLine;
using Archcraft.App.UseCases;
using Archcraft.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Archcraft.Cli.Commands;

public static class ValidateCommand
{
    public static Command Build(IServiceProvider services)
    {
        Argument<FileInfo> fileArg = new("project-file")
        {
            Description = "Path to project.yaml"
        };

        Command command = new("validate", "Validate a project file without running it")
        {
            fileArg
        };

        command.SetAction(async (ParseResult result, CancellationToken ct) =>
        {
            FileInfo file = result.GetValue(fileArg)!;
            ValidateProjectUseCase useCase = services.GetRequiredService<ValidateProjectUseCase>();
            ValidationResult validation = await useCase.ExecuteAsync(file.FullName, ct);

            if (validation.IsValid)
            {
                Console.WriteLine($"Project '{file.Name}' is valid.");
                return 0;
            }

            Console.Error.WriteLine("Validation failed:");
            foreach (string error in validation.Errors)
                Console.Error.WriteLine($"  - {error}");

            return 1;
        });

        return command;
    }
}
