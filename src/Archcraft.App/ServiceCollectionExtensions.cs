using Archcraft.App.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Archcraft.App;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArchcraftApp(this IServiceCollection services)
    {
        services.AddTransient<RunProjectUseCase>();
        services.AddTransient<ValidateProjectUseCase>();
        return services;
    }
}
