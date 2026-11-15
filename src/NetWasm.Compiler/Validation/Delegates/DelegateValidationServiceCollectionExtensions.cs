// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Validation.Delegates;

internal static class DelegateValidationServiceCollectionExtensions
{
    public static IServiceCollection AddDelegateValidation(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDelegateBindingInvariantValidator,
            DelegateBindingInvariantValidator>();
        return services;
    }
}
