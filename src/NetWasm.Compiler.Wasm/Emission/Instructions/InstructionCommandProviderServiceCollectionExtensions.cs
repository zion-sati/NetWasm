// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal static class InstructionCommandProviderServiceCollectionExtensions
{
    public static IServiceCollection AddInstructionCommandProvider<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        this IServiceCollection services)
        where T : class, IInstructionCommandProvider
    {
        services.AddSingleton<T>();
        services.AddSingleton<IInstructionCommandProvider>(provider =>
            provider.GetRequiredService<T>());
        return services;
    }
}
