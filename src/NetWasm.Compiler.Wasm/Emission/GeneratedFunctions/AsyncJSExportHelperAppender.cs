using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSExportHelperAppender(
    IAsyncJSExportStatusEmitter statuses,
    IAsyncJSExportResultEmitter results,
    IAsyncJSExportCompletionEmitter completions,
    IAsyncJSExportResultTypeResolver resultTypes,
    IManagedBoundaryPlanBuilder boundaries) : IAsyncJSExportHelperAppender
{
    public void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<string, int> indices,
        JavaScriptAsyncMethodBinding binding,
        ManagedAsyncBoundaryNames names,
        ManagedAsyncBoundaryKinds kinds,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        var statusName = names.Status;
        indices.Add(statusName, importCount + functions.Count);
        var statusIndex = importCount + functions.Count;
        functions.Add(new WasmFunctionDefinition(
            statusName,
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.I4),
            statuses.Emit(binding)));
        boundaryEntries.Add(boundaries.Build(new(
            [.. functions],
            importCount,
            statusIndex,
            statusName,
            kinds.Observation,
            true)));
        if (binding.Return.HasResult)
        {
            var resultName = names.Result ?? throw new InvalidOperationException(
                "an asynchronous result boundary requires a result name");
            indices.Add(resultName, importCount + functions.Count);
            var resultIndex = importCount + functions.Count;
            functions.Add(new WasmFunctionDefinition(
                resultName,
                resultTypes.Resolve(binding),
                results.Emit(binding)));
            boundaryEntries.Add(boundaries.Build(new(
                [.. functions],
                importCount,
                resultIndex,
                resultName,
                kinds.Observation,
                true)));
        }
        var completeName = names.Complete;
        indices.Add(completeName, importCount + functions.Count);
        var completeIndex = importCount + functions.Count;
        functions.Add(new WasmFunctionDefinition(
            completeName,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4),
            completions.Emit()));
        boundaryEntries.Add(boundaries.Build(new(
            [.. functions],
            importCount,
            completeIndex,
            completeName,
            kinds.Completion,
            true)));
    }
}
