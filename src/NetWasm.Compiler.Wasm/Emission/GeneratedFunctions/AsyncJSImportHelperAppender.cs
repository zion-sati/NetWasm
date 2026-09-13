using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSImportHelperAppender(
    IAsyncJSImportResolveEmitter resolvers,
    IAsyncJSImportRejectEmitter rejectors,
    IAsyncJSImportCancelEmitter cancellers,
    IAsyncJSImportResolveTypeResolver resolveTypes,
    IManagedBoundaryPlanBuilder boundaries) : IAsyncJSImportHelperAppender
{
    public void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        ImmutableDictionary<string, int>.Builder indices,
        JavaScriptAsyncMethodBinding binding,
        IFunctionIndexResolver functionIndices,
        IList<ManagedBoundaryPlanEntry> boundaryEntries)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        var resolveName = JavaScriptAsyncAbiNames.Resolve(binding.Method);
        indices.Add(resolveName, importCount + functions.Count);
        var resolveIndex = importCount + functions.Count;
        functions.Add(new WasmFunctionDefinition(
            resolveName,
            resolveTypes.Resolve(binding),
            resolvers.Emit(binding, functionIndices)));
        AddBoundary(
            functions,
            importCount,
            resolveIndex,
            resolveName,
            boundaryEntries);

        var rejectName = JavaScriptAsyncAbiNames.Reject(binding.Method);
        indices.Add(rejectName, importCount + functions.Count);
        var rejectIndex = importCount + functions.Count;
        functions.Add(new WasmFunctionDefinition(
            rejectName,
            CompletionType(),
            rejectors.Emit(binding, functionIndices)));
        AddBoundary(
            functions,
            importCount,
            rejectIndex,
            rejectName,
            boundaryEntries);

        var cancelName = JavaScriptAsyncAbiNames.Cancel(binding.Method);
        indices.Add(cancelName, importCount + functions.Count);
        var cancelIndex = importCount + functions.Count;
        functions.Add(new WasmFunctionDefinition(
            cancelName,
            CompletionType(),
            cancellers.Emit(binding, functionIndices)));
        AddBoundary(
            functions,
            importCount,
            cancelIndex,
            cancelName,
            boundaryEntries);
    }

    private void AddBoundary(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        int functionIndex,
        string name,
        ICollection<ManagedBoundaryPlanEntry> entries) =>
        entries.Add(boundaries.Build(new(
            [.. functions],
            importCount,
            functionIndex,
            name,
            ManagedBoundaryKind.AsynchronousImportCompletion,
            true)));

    private static WasmFunctionType CompletionType() =>
        WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4);
}
