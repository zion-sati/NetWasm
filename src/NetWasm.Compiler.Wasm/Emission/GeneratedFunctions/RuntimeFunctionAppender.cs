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
    // A callback-lifted wasi:cli/run's callback, and the exit code its task.return reads.
    internal const string EntryCallbackExport = "netwasm.entry.callback";
    internal const string EntryExitCodeExport = "netwasm.entry.exit_code";

    public RuntimeFunctionAppendResult Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        ImmutableArray<FilterFunclet> filterFunclets,
        IReadOnlyDictionary<int, int> filterIndices,
        MethodDefinitionModel? entryPoint,
        RuntimeInitializationPlan initialization,
        WasmEntryPointProfile entryPointProfile,
        JavaScriptAsyncMethodBinding? asyncBinding,
        IDictionary<string, int> asyncHelperIndices,
        IFunctionIndexResolver functionIndices,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries,
        EntityKey? entryPointArgumentFactory = null,
        EntityKey? localTimePreflight = null,
        EntityKey? processExit = null)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(filterIndices);
        if (entryPointProfile != WasmEntryPointProfile.None)
        {
            ArgumentNullException.ThrowIfNull(entryPoint);
        }
        else if (entryPoint is not null)
        {
            throw new ArgumentException("Library modules cannot declare an entry point.", nameof(entryPoint));
        }
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
            .Select(descriptor => new FinalizerDispatchPlan(
                descriptor.TypeId,
                descriptor.Finalizer!))
            .Concat(descriptors.ConstructedTypeDescriptors
                .Where(descriptor => descriptor.Finalizer is not null)
                .Select(descriptor => new FinalizerDispatchPlan(
                    descriptor.TypeId,
                    descriptor.Finalizer!)))
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

        if (entryPoint is null)
        {
            return new(filterDispatcherIndex, finalizerDispatcherIndex, null,
                finalizableTypes.Length != 0);
        }

        int entryPointIndex;
        if (entryPointProfile == WasmEntryPointProfile.Process)
        {
            // An asynchronous Main is a callback-lifted wasi:cli/run: the entry starts it through
            // AsyncEntryPoint.Start and returns the callback code.
            var commandEntry = asyncBinding?.CommandEntry;
            if (asyncBinding is not null && commandEntry is null)
            {
                throw new InvalidOperationException(
                    "An asynchronous process entry point returns Task or Task<int>.");
            }
            entryPointIndex = outwardMethods.Append(new(
                functions,
                importCount,
                asyncHelperIndices,
                "netwasm.entry",
                "run",
                entryPoint,
                null,
                null,
                null,
                initialization,
                finalizableTypes.Length != 0,
                true,
                ManagedBoundaryKind.ProcessEntryPoint,
                functionIndices,
                boundaryEntries,
                entryPointArgumentFactory,
                EntryCompletion: commandEntry?.Start,
                LocalTimePreflight: localTimePreflight,
                // A callback-lifted entry exits when CoreLib completes its task.
                ProcessExit: commandEntry is null ? processExit : null));
            if (commandEntry is not null)
            {
                // The callback and the exit code have the entry's terminal boundary, not its
                // initialization: the start initialized the runtime.
                AppendEntryExport(EntryCallbackExport, commandEntry.Callback);
                AppendEntryExport(EntryExitCodeExport, commandEntry.ExitCode);
            }

            void AppendEntryExport(string name, MethodInstanceModel method) =>
                asyncHelperIndices.Add(name, outwardMethods.Append(new(
                    functions,
                    importCount,
                    asyncHelperIndices,
                    name,
                    name,
                    method.Definition,
                    null,
                    null,
                    null,
                    initialization,
                    finalizableTypes.Length != 0,
                    true,
                    ManagedBoundaryKind.ProcessEntryPoint,
                    functionIndices,
                    boundaryEntries,
                    InitializesRuntime: false)));
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
