// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
