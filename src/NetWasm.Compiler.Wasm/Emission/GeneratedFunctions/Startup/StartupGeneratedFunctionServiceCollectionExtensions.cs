using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

internal static class StartupGeneratedFunctionServiceCollectionExtensions
{
    public static IServiceCollection AddStartupGeneratedFunctionEmission(
        this IServiceCollection services)
    {
        services.AddSingleton<IStartupPreflightMethodSelector,
            StartupPreflightMethodSelector>();
        services.AddSingleton<IStartupPreflightCallEmitter,
            StartupPreflightCallEmitter>();
        return services;
    }
}
