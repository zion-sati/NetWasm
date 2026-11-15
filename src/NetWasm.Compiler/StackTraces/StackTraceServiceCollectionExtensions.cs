// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.StackTraces;

internal static class StackTraceServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerStackTraces(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IStackTraceReachabilityRootProvider,
            StackTraceReachabilityRootProvider>();
        services.AddSingleton<ISourceDocumentIdentityFormatter,
            SourceDocumentIdentityFormatter>();
        services.AddSingleton<IStackTraceSourceLocationReader,
            StackTraceSourceLocationReader>();
        return services;
    }
}
