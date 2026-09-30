using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class RequestedExportFunctionAppender(
    IOutwardMethodFunctionAppender outwardMethods) :
    IRequestedExportFunctionAppender
{
    public void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<string, int> requestedExportIndices,
        IDictionary<string, int> asyncHelperIndices,
        string exportName,
        MethodInstanceModel method,
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
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(asyncBindings);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        var selectedDefinition = method.Definition with
        {
            Signature = method.Signature,
        };
        asyncBindings.TryGetValue(method.Definition.Key, out var asyncBinding);
        var requestedExportIndex = outwardMethods.Append(new(
            functions,
            importCount,
            asyncHelperIndices,
            $"netwasm.export.{exportName}",
            exportName,
            selectedDefinition,
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
