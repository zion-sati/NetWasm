// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal static class ArrayMetadataServiceCollectionExtensions
{
    public static IServiceCollection AddArrayMetadataCaching(this IServiceCollection services)
    {
        services.AddSingleton<IArrayMetadataCachingPlanner, ArrayMetadataCachingPlanner>();
        services.AddSingleton<IArrayLengthRunPlanner, ArrayLengthRunPlanner>();
        services.AddSingleton<IArrayStoreCheckPlanner, ArrayStoreCheckPlanner>();
        services.AddSingleton<IArrayMetadataSnapshotEmitter, ArrayMetadataSnapshotEmitter>();
        services.AddSingleton<IArrayElementAddressEmitter, ArrayElementAddressEmitter>();
        return services;
    }
}
