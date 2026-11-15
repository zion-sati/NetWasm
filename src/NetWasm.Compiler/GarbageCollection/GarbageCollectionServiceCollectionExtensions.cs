// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.GarbageCollection;

internal static class GarbageCollectionServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerGarbageCollection(
        this IServiceCollection services)
    {
        services.AddSingleton<IRootMapAnalyzerFactory, RootMapAnalyzerFactory>();
        services.AddSingleton<IRootSlotAllocator, RootSlotAllocator>();
        services.AddSingleton<IRootDecisionClassifierFactory,
            RootDecisionClassifierFactory>();
        services.AddSingleton<ICalleeArgumentRootingClassifierFactory,
            CalleeArgumentRootingClassifierFactory>();
        services.AddSingleton<IRuntimeAllocationSafepointClassifier,
            RuntimeAllocationSafepointClassifier>();
        return services;
    }
}
