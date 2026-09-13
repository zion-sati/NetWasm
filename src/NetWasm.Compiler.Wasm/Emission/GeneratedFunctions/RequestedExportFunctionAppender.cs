using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class RequestedExportFunctionAppender(
    IMethodRepository methods,
    IOutwardMethodFunctionAppender outwardMethods) :
    IRequestedExportFunctionAppender
{
    public void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<string, int> requestedExportIndices,
        IDictionary<string, int> asyncHelperIndices,
        string exportName,
        EntityKey methodKey,
        IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding> asyncBindings,
        RuntimeInitializationPlan initialization,
        bool hasFinalizers,
        WasmModuleProfile profile,
        IFunctionIndexResolver functionIndices,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(requestedExportIndices);
        ArgumentNullException.ThrowIfNull(asyncHelperIndices);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportName);
        ArgumentNullException.ThrowIfNull(asyncBindings);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        var method = methods.GetMethod(methodKey);
        asyncBindings.TryGetValue(methodKey, out var asyncBinding);
        var requestedExportIndex = outwardMethods.Append(new(
            functions,
            importCount,
            asyncHelperIndices,
            $"netwasm.export.{exportName}",
            exportName,
            method,
            asyncBinding,
            asyncBinding is null
                ? null
                : ManagedAsyncBoundaryNames.ForExport(asyncBinding),
            asyncBinding is null
                ? null
                : ManagedAsyncBoundaryKinds.Export with
                {
                    Start = profile == WasmModuleProfile.CoreApplication
                        ? ManagedBoundaryKind.AsynchronousExportStart
                        : ManagedBoundaryKind.ComponentAdapter,
                },
            initialization,
            hasFinalizers,
            profile == WasmModuleProfile.CoreApplication,
            profile == WasmModuleProfile.CoreApplication
                ? ManagedBoundaryKind.SynchronousExport
                : ManagedBoundaryKind.ComponentAdapter,
            functionIndices,
            boundaryEntries));
        requestedExportIndices.Add(exportName, requestedExportIndex);
    }
}
