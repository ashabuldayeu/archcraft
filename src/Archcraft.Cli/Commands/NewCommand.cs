using System.CommandLine;
using Archcraft.App.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Archcraft.Cli.Commands;

public static class NewCommand
{
    public static Command Build(IServiceProvider services)
    {
        Argument<string> nameArg = new("name")
        {
            Description = "Project name (used as the 'name' field in project.yaml)"
        };

        Option<int> servicesOpt = new("--services")
        {
            Description = "Number of synthetic services in the call chain (minimum 1)"
        };
        servicesOpt.DefaultValueFactory = _ => 2;

        Option<string> dbOpt = new("--db")
        {
            Description = "Database at the end of the chain: postgres | redis | none"
        };
        dbOpt.DefaultValueFactory = _ => "postgres";

        Option<int> replicasOpt = new("--replicas")
        {
            Description = "Number of replicas per synthetic service (minimum 1)"
        };
        replicasOpt.DefaultValueFactory = _ => 3;

        Command command = new("new", "Scaffold a new archcraft project in the current directory")
        {
            nameArg,
            servicesOpt,
            dbOpt,
            replicasOpt
        };

        command.SetAction(async (ParseResult result, CancellationToken ct) =>
        {
            string name = result.GetValue(nameArg)!;
            int svcCount = result.GetValue(servicesOpt);
            string db = result.GetValue(dbOpt)!;
            int replicas = result.GetValue(replicasOpt);

            if (svcCount < 1)
            {
                Console.Error.WriteLine("Error: --services must be at least 1.");
                return 1;
            }

            if (replicas < 1)
            {
                Console.Error.WriteLine("Error: --replicas must be at least 1.");
                return 1;
            }

            if (db != "postgres" && db != "redis" && db != "none")
            {
                Console.Error.WriteLine("Error: --db must be one of: postgres, redis, none.");
                return 1;
            }

            await using AsyncServiceScope scope = services.CreateAsyncScope();
            NewProjectUseCase useCase = scope.ServiceProvider.GetRequiredService<NewProjectUseCase>();
            return await useCase.ExecuteAsync(name, svcCount, db, replicas, ct);
        });

        return command;
    }
}
