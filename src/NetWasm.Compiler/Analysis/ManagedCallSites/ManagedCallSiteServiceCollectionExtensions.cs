// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Analysis.ManagedCallSites;

internal static class ManagedCallSiteServiceCollectionExtensions
{
    public static IServiceCollection AddManagedCallSiteAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<IManagedCallSiteLedgerWriter, ManagedCallSiteLedgerWriter>();
        return services;
    }
}
