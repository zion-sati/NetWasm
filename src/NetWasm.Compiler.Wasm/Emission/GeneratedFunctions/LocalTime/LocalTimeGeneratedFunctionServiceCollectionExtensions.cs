using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;

internal static class LocalTimeGeneratedFunctionServiceCollectionExtensions
{
    public static IServiceCollection AddLocalTimeGeneratedFunctionEmission(
        this IServiceCollection services)
    {
        services.AddSingleton<ILocalTimePreflightMethodSelector,
            LocalTimePreflightMethodSelector>();
        services.AddSingleton<ILocalTimePreflightCallEmitter,
            LocalTimePreflightCallEmitter>();
        return services;
    }
}
