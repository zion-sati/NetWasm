// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.IntermediateRepresentation.Calls;

internal static class ManagedCallSiteServiceCollectionExtensions
{
    public static IServiceCollection AddManagedCallSites(this IServiceCollection services)
    {
        services.AddSingleton<IManagedMethodIdentityFactory, ManagedMethodIdentityFactory>();
        services.AddSingleton<IManagedCallSiteFactory, ManagedCallSiteFactory>();
        return services;
    }
}
