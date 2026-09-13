using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class RuntimeFunctionAppender(
    ITypeDescriptorSource descriptors,
    IFilterDispatcherEmitter filters,
    IFinalizerDispatcherEmitter finalizers,
    IEntryPointEmitter entryPoints,
    IManagedMethodFunctionTypeResolver functionTypes,
    IOutwardMethodFunctionAppender outwardMethods,
    IManagedBoundaryPlanBuilder boundaries) : IRuntimeFunctionAppender
{
    public RuntimeFunctionAppendResult Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        ImmutableArray<FilterFunclet> filterFunclets,
        IReadOnlyDictionary<int, int> filterIndices,
        MethodDefinitionModel entryPoint,
        RuntimeInitializationPlan initialization,
        WasmEntryPointProfile entryPointProfile,
        JavaScriptAsyncMethodBinding? asyncBinding,
        IDictionary<string, int> asyncHelperIndices,
        IFunctionIndexResolver functionIndices,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries,
        EntityKey? entryPointArgumentFactory = null)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(filterIndices);
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(asyncHelperIndices);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        var filterDispatcherIndex = importCount + functions.Count;
        functions.Add(new WasmFunctionDefinition(
            RuntimeAbi.ManagedFilterDispatcher,
            WasmFunctionType.Create(
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress),
            filters.Emit(filterFunclets, filterIndices)));
        AddBoundary(
            functions,
            importCount,
            filterDispatcherIndex,
            RuntimeAbi.ManagedFilterDispatcher,
            ManagedBoundaryKind.InternalRuntimeDispatch,
            false,
            boundaryEntries);

        var finalizableTypes = descriptors.TypeDescriptors
            .Where(descriptor => descriptor.Finalizer is not null)
            .OrderBy(descriptor => descriptor.TypeId)
            .ToArray();
        var finalizerDispatcherIndex = importCount + functions.Count;
        functions.Add(new WasmFunctionDefinition(
            RuntimeAbi.ManagedFinalizerDispatcher,
            WasmFunctionType.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4),
            finalizers.Emit(finalizableTypes, functionIndices)));
        AddBoundary(
            functions,
            importCount,
            finalizerDispatcherIndex,
            RuntimeAbi.ManagedFinalizerDispatcher,
            ManagedBoundaryKind.InternalRuntimeDispatch,
            false,
            boundaryEntries);

        int entryPointIndex;
        if (entryPointProfile == WasmEntryPointProfile.Process)
        {
            entryPointIndex = outwardMethods.Append(new(
                functions,
                importCount,
                asyncHelperIndices,
                "netwasm.entry",
                "run",
                entryPoint,
                asyncBinding,
                asyncBinding is null
                    ? null
                    : ManagedAsyncBoundaryNames.ForProcess(asyncBinding),
                asyncBinding is null
                    ? null
                    : ManagedAsyncBoundaryKinds.Process,
                initialization,
                finalizableTypes.Length != 0,
                true,
                ManagedBoundaryKind.ProcessEntryPoint,
                functionIndices,
                boundaryEntries,
                entryPointArgumentFactory));
        }
        else
        {
            entryPointIndex = importCount + functions.Count;
            functions.Add(new WasmFunctionDefinition(
                "netwasm.entry",
                functionTypes.Resolve(entryPoint),
                entryPoints.Emit(
                    entryPoint,
                    initialization,
                    finalizableTypes.Length != 0,
                    functionIndices,
                    reportTerminalExceptions: false)));
        }

        return new(
            filterDispatcherIndex,
            finalizerDispatcherIndex,
            entryPointIndex,
            finalizableTypes.Length != 0);
    }

    private void AddBoundary(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        int functionIndex,
        string exportName,
        ManagedBoundaryKind kind,
        bool outwardFacing,
        ICollection<ManagedBoundaryPlanEntry> entries) =>
        entries.Add(boundaries.Build(new(
            [.. functions],
            importCount,
            functionIndex,
            exportName,
            kind,
            outwardFacing)));
}
