// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

internal static class StartupGeneratedFunctionServiceCollectionExtensions
{
    public static IServiceCollection AddStartupGeneratedFunctionEmission(
        this IServiceCollection services)
    {
        services.AddSingleton<IStartupPreflightMethodSelector,
            StartupPreflightMethodSelector>();
        return services;
    }
}
