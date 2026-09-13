using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission;

public static class NetWasmEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddNetWasmEmission(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWasmModuleEmitterFactory, WasmModuleEmitterFactory>();
        return services;
    }
}
