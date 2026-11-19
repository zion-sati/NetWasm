// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class ComponentFunctionAppender(
    IComponentBoundaryEmitter components,
    IManagedBoundaryPlanBuilder boundaries) : IComponentFunctionAppender
{
    public ImmutableArray<string> Append(
        IList<WasmFunctionDefinition> functions,
        ICollection<WasmExport> exports,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries,
        WasmEmissionRequest request,
        WasmTarget target,
        RuntimeInitializationPlan initialization,
        int importCount,
        IReadOnlyDictionary<string, int> requestedExportIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(boundaryEntries);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(requestedExportIndices);

        var component = components.Emit(
            request,
            target,
            initialization,
            importCount,
            functions.Count,
            requestedExportIndices);
        foreach (var function in component.Functions)
        {
            functions.Add(function);
        }
        foreach (var export in component.Exports)
        {
            exports.Add(export);
        }
        foreach (var componentExport in component.Exports.Where(export =>
                     export.Kind == WasmExportKind.Function))
        {
            var functionName = functions[componentExport.Index - importCount].Name;
            var kind = functionName switch
            {
                "component.initialize" => ManagedBoundaryKind.ComponentInitialize,
                "component.realloc" => ManagedBoundaryKind.ComponentReallocate,
                _ => ManagedBoundaryKind.ComponentPostReturn,
            };
            boundaryEntries.Add(boundaries.Build(new(
                [.. functions],
                importCount,
                componentExport.Index,
                componentExport.Name,
                kind,
                true)));
        }
        return component.RuntimeFeatures;
    }
}
