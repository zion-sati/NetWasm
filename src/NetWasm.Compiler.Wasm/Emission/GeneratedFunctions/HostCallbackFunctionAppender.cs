using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class HostCallbackFunctionAppender(
    IHostCallbackFunctionEmitter callbacks,
    IHostCallbackFunctionTypeResolver functionTypes,
    IManagedBoundaryPlanBuilder boundaries) : IHostCallbackFunctionAppender
{
    public void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<string, int> indices,
        HostCallbackDeclaration callback,
        FunctionIndexMap functionIndices,
        RuntimeInitializationPlan initialization,
        InteropImportPlan interopImports,
        RuntimeImportSelection runtimeImportSelection,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(interopImports);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        var callbackIndex = importCount + functions.Count;
        indices.Add(callback.ExportName, callbackIndex);
        functions.Add(new WasmFunctionDefinition(
            callback.ExportName,
            functionTypes.Resolve(callback.Invoke),
            callbacks.Emit(
                callback,
                functionIndices,
                initialization,
                interopImports,
                runtimeImportSelection)));
        boundaryEntries.Add(boundaries.Build(new(
            [.. functions],
            importCount,
            callbackIndex,
            callback.ExportName,
            ManagedBoundaryKind.HostCallback,
            true)));
    }
}
