// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.Support;

internal static class SupportServiceCollectionExtensions
{
    public static IServiceCollection AddWasmEmissionSupport(
        this IServiceCollection services)
    {
        services.AddSingleton<AddressInstructionEmitter>();
        services.AddSingleton<IAddressInstructionEmitter>(provider =>
            provider.GetRequiredService<AddressInstructionEmitter>());
        return services;
    }
}
