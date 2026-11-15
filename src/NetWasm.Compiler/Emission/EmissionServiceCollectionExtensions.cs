// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        services.AddSingleton<WasmMethodLowerer>();
        services.AddSingleton<IWasmMethodLowerer>(static provider =>
            provider.GetRequiredService<WasmMethodLowerer>());
        services.AddSingleton<IWasmMethodProgramBuilder, WasmMethodProgramBuilder>();
        services.AddSingleton<IManagedWasmEmitterFactory, ManagedWasmEmitterFactory>();
        return services;
    }
}
