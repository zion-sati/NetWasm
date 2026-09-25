using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Wasm.Emission;

internal enum RuntimeImportSymbol
{
    Initialize,
    Allocate,
    Collect,
    RegisterType,
    RegisterStaticRoot,
    RootFrameEnter,
    RootFrameLeave,
    ValueFrameEnter,
    ValueFrameLeave,
    BeginThrow,
    BeginRethrow,
    AllocateReferenceArray,
    AllocateString,
    SuppressFinalize,
    ReRegisterForFinalize,
    ReportUnobservedTaskException,
    IsAssignable,
    EndCatch,
    ExceptionFrameEnter,
    ExceptionFrameLeave,
    ExceptionFrameTargetClause,
    FinalizerSafepoint,
    RegisterValueType,
    AllocateValueArray,
    AllocateRectangularArray,
    AllocateBoundedRectangularArray,
    ArrayRank,
    ArrayGetLength,
    ArrayGetLowerBound,
    ArrayGetValue,
    ArrayCopy,
    ArrayClear,
    ArrayClone,
    ExceptionFrameSetEnvironment,
    GetTypeObject,
    HandleNew,
    HandleGet,
    HandleRelease,
    WeakHandleCreate,
    WeakHandleGet,
    WeakHandleSet,
    WeakHandleRelease,
    GcHandleCreate,
    GcHandleGet,
    GcHandleAddress,
    GcHandleSet,
    GcHandleRelease,
    GcGetMetric,
    GcMetricIsSupported,
    GcWaitForPendingFinalizers,
    ObjectIdentityHash,
    NativeAlloc,
    NativeRealloc,
    NativeFree,
    NativeAlignedAlloc,
    NativeAlignedRealloc,
    NativeAlignedFree,
    ComponentReallocate,
    ComponentFree,
    ManagedTerminalExceptionReport,
    StackTraceFrameEnter,
    StackTraceFrameLeave,
    StackTraceInitialize,
}

internal readonly record struct RuntimeImportBinding(
    RuntimeImportSymbol Symbol,
    WasmFunctionImport Import);

internal sealed class RuntimeImportCatalog : IRuntimeImportResolver
{
    private readonly ImmutableArray<RuntimeImportBinding> _bindings;
    private readonly ImmutableDictionary<RuntimeImportSymbol, int> _indices;

    public RuntimeImportCatalog(IEnumerable<RuntimeImportBinding> bindings)
    {
        var orderedBindings = bindings.ToImmutableArray();
        var indices = ImmutableDictionary.CreateBuilder<RuntimeImportSymbol, int>();

        for (var index = 0; index < orderedBindings.Length; index++)
        {
            if (!indices.TryAdd(orderedBindings[index].Symbol, index))
            {
                throw new InvalidOperationException(
                    $"runtime import '{orderedBindings[index].Symbol}' is registered more than once");
            }
        }

        var symbols = Enum.GetValues<RuntimeImportSymbol>();
        var missing = symbols.Where(symbol => !indices.ContainsKey(symbol)).ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                $"runtime import catalog is missing: {string.Join(", ", missing)}");
        }

        _bindings = orderedBindings;
        Imports = [.. orderedBindings.Select(binding => binding.Import)];
        _indices = indices.ToImmutable();
    }

    public ImmutableArray<WasmFunctionImport> Imports { get; }

    public ImmutableArray<WasmFunctionImport> Resolve(WasmModuleProfile profile) =>
        Resolve(DefaultSelection(profile));

    public ImmutableArray<WasmFunctionImport> Resolve(RuntimeImportSelection selection) =>
        _bindings
            .Where(binding => IsIncluded(binding.Symbol, selection))
            .Select(static binding => binding.Import)
            .ToImmutableArray();

    public int Resolve(RuntimeImportSymbol symbol) => _indices[symbol];

    public int Resolve(RuntimeImportSymbol symbol, WasmModuleProfile profile)
        => Resolve(symbol, DefaultSelection(profile));

    public int Resolve(RuntimeImportSymbol symbol, RuntimeImportSelection selection)
    {
        var index = 0;
        foreach (var binding in _bindings)
        {
            if (!IsIncluded(binding.Symbol, selection))
            {
                continue;
            }

            if (binding.Symbol == symbol)
            {
                return index;
            }

            index++;
        }

        throw new InvalidOperationException($"Runtime import '{symbol}' is unavailable for the selected module profile.");
    }

    private static RuntimeImportSelection DefaultSelection(WasmModuleProfile profile) =>
        new(profile, profile == WasmModuleProfile.CoreApplication);

    private static bool IsIncluded(
        RuntimeImportSymbol symbol,
        RuntimeImportSelection selection) =>
        (selection.IncludeStackTrace || symbol is not
            (RuntimeImportSymbol.StackTraceFrameEnter or
             RuntimeImportSymbol.StackTraceFrameLeave or
             RuntimeImportSymbol.StackTraceInitialize)) &&
        (selection.ModuleProfile == WasmModuleProfile.ComponentCoreModule
            ? symbol != RuntimeImportSymbol.ManagedTerminalExceptionReport ||
              selection.IncludeTerminalExceptionReporter
            : symbol is not RuntimeImportSymbol.ComponentReallocate
                and not RuntimeImportSymbol.ComponentFree);
}
