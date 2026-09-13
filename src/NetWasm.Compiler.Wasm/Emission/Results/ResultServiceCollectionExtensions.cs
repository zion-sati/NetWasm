using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.Results;

internal static class ResultServiceCollectionExtensions
{
    public static IServiceCollection AddWasmEmissionResults(
        this IServiceCollection services)
    {
        services.AddSingleton<IWasmModuleEmissionResultBuilder,
            WasmModuleEmissionResultBuilder>();
        return services;
    }
}
