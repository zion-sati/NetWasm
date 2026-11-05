using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission;

public static class NetWasmEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddNetWasmEmission(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWasmModuleEmitterFactory, WasmModuleEmitterFactory>();
        // Compiled-unit storage. Desktop: memory-mapped segments; hosts without files
        // register their own first.
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitStoreLoader,
            NetWasm.Compiler.Wasm.Units.MappedFileUnitStoreLoader>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitStoreCommitter,
            NetWasm.Compiler.Wasm.Units.FileUnitStoreCommitter>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitKeptCompileLoader,
            NetWasm.Compiler.Wasm.Units.FileUnitKeptCompileLoader>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitKeptCompileCommitter,
            NetWasm.Compiler.Wasm.Units.FileUnitKeptCompileCommitter>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitInputsLoader,
            NetWasm.Compiler.Wasm.Units.FileUnitInputsLoader>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitInputsCommitter,
            NetWasm.Compiler.Wasm.Units.FileUnitInputsCommitter>();
        return services;
    }
}
