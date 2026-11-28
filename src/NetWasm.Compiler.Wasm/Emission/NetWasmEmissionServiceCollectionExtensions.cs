// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        // Lowering, composed here, reports a method's defects; emission's module services take it again.
        services.TryAddSingleton<IMethodDefectExceptionFactory, MethodDefectExceptionFactory>();
        // The engines' function limits, which every module and every patched function meets; a composition may
        // register others first (tests do, to reach them cheaply).
        services.TryAddSingleton(new NetWasm.Compiler.Wasm.ModuleEncoding.WasmFunctionLimits());
        services.TryAddSingleton<NetWasm.Compiler.Wasm.ModuleEncoding.IWasmFunctionLimitValidator>(static provider =>
            new NetWasm.Compiler.Wasm.ModuleEncoding.WasmFunctionLimitValidator(
                provider.GetRequiredService<NetWasm.Compiler.Wasm.ModuleEncoding.WasmFunctionLimits>()));
        // Compiled-unit storage. Desktop: memory-mapped segments, which the OS is asked to
        // read ahead; hosts without files register their own first.
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.INativeExportResolver,
            NetWasm.Compiler.Wasm.Units.NativeLibraryExportResolver>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitMemoryPrefetcherFactory,
            NetWasm.Compiler.Wasm.Units.UnitMemoryPrefetcherFactory>();
        services.TryAddSingleton(provider => provider.GetRequiredService<NetWasm.Compiler.Wasm.Units.IUnitMemoryPrefetcherFactory>()
            .Create(NetWasm.Compiler.Wasm.Units.UnitMemoryPrefetcherFactory.Host()));
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitFileDeleter, NetWasm.Compiler.Wasm.Units.UnitFileDeleter>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitStoreLoader,
            NetWasm.Compiler.Wasm.Units.MappedFileUnitStoreLoader>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitStoreCommitter,
            NetWasm.Compiler.Wasm.Units.FileUnitStoreCommitter>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Units.IUnitSegmentSealer,
            NetWasm.Compiler.Wasm.Units.FileUnitSegmentSealer>();
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
