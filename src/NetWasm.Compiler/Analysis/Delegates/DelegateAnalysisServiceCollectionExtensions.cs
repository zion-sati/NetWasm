// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Analysis.Delegates;

internal static class DelegateAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddDelegateAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDelegateBindingPlannerFactory,
            DelegateBindingPlannerFactory>();
        return services;
    }
}
