using System;
using System.Collections.Generic;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class NativeCallbackFunctionAppender(
    INativeCallbackThunkEmitter thunks,
    IManagedBoundaryPlanBuilder boundaries) : INativeCallbackFunctionAppender
{
    public void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<string, int> indices,
        NativeCallbackPlan callbacks,
        ModuleDataPlan moduleData,
        RuntimeImportSelection runtimeImportSelection,
        IFunctionIndexResolver functionIndices,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(moduleData);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        foreach (var callback in callbacks.Methods)
        {
            var functionIndex = importCount + functions.Count;
            indices.Add(callback.ThunkExportName, functionIndex);
            var signature = callback.Abi.PhysicalSignature;
            functions.Add(new(
                callback.ThunkExportName,
                new(signature.ParameterTypes, signature.ReturnType),
                thunks.Emit(
                    callback,
                    moduleData,
                    runtimeImportSelection,
                    functionIndices)));
            boundaryEntries.Add(boundaries.Build(new(
                [.. functions],
                importCount,
                functionIndex,
                callback.ThunkExportName,
                ManagedBoundaryKind.NativeCallback,
                true)));
        }
    }
}
