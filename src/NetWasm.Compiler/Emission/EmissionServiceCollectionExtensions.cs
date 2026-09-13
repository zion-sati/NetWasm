using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Emission;

internal static class EmissionServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerEmission(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddNetWasmEmission();
        services.AddSingleton<IWasmMethodLowerer, WasmMethodLowerer>();
        services.AddSingleton<IWasmMethodProgramBuilder, WasmMethodProgramBuilder>();
        services.AddSingleton<IManagedWasmEmitterFactory, ManagedWasmEmitterFactory>();
        return services;
    }
}
