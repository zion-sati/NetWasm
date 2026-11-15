// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Validation.Delegates;

namespace NetWasm.Compiler.Validation;

internal static class ValidationServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDelegateValidation();
        services.AddSingleton<IMetadataInvariantValidator,
            MetadataInvariantValidator>();
        services.AddSingleton<IStructuredProgramInvariantValidator,
            StructuredProgramInvariantValidator>();
        services.AddSingleton<IReachabilityInvariantValidator,
            ReachabilityInvariantValidator>();
        services.AddSingleton<IRootMapInvariantValidator,
            RootMapInvariantValidator>();
        services.AddSingleton<IWasmEmissionInvariantValidator,
            WasmEmissionInvariantValidator>();
        services.AddSingleton<ILayoutInvariantValidator,
            LayoutInvariantValidator>();
        services.AddSingleton<ICompilerInvariantExceptionFactory,
            CompilerInvariantExceptionFactory>();
        services.AddSingleton<ICompilerComplexityInvariantValidator,
            CompilerComplexityInvariantValidator>();
        return services;
    }
}
