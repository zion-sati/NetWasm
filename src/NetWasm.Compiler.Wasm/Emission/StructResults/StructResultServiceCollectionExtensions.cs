// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

internal static class StructResultServiceCollectionExtensions
{
    public static IServiceCollection AddStructResults(this IServiceCollection services)
    {
        // A value type of at most four scalar fields is returned in results and passed by its fields.
        services.TryAddSingleton<IStructResultClassifier, StructResultClassifier>();
        services.TryAddSingleton<IStructResultScratchPlanner, StructResultScratchPlanner>();
        services.TryAddSingleton<IStructResultEmitter, StructResultEmitter>();
        services.TryAddSingleton<IValueTypeArgumentConventionFactory, ValueTypeArgumentConventionFactory>();
        services.TryAddSingleton<IValueTypeResultConventionFactory, ValueTypeResultConventionFactory>();
        services.TryAddSingleton<IValueTypeReturnConventionFactory, ValueTypeReturnConventionFactory>();
        services.TryAddSingleton<IStructConventionCallClassifier, StructConventionCallClassifier>();
        services.TryAddSingleton<IFieldArgumentPlanner, FieldArgumentPlanner>();
        services.TryAddSingleton<Methods.IStructLanePlanner, Methods.StructLanePlanner>();
        return services;
    }
}
