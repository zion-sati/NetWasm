using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class ModuleExportCollector : IModuleExportCollector
{
    public IReadOnlyList<WasmExport> Collect(
        WasmEntryPointProfile entryPointProfile,
        int entryPointIndex,
        int filterDispatcherIndex,
        int finalizerDispatcherIndex,
        IReadOnlyDictionary<string, int> requestedExports,
        IReadOnlyDictionary<string, int> hostCallbacks,
        IReadOnlyDictionary<string, int> asyncImports,
        IReadOnlyDictionary<string, int> asyncExportHelpers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryPointIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(filterDispatcherIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(finalizerDispatcherIndex);
        ArgumentNullException.ThrowIfNull(requestedExports);
        ArgumentNullException.ThrowIfNull(hostCallbacks);
        ArgumentNullException.ThrowIfNull(asyncImports);
        ArgumentNullException.ThrowIfNull(asyncExportHelpers);

        var exports = new List<WasmExport>
        {
            new(RuntimeAbi.ManagedFilterDispatcher, filterDispatcherIndex),
            new(RuntimeAbi.ManagedFinalizerDispatcher, finalizerDispatcherIndex),
        };
        if (entryPointProfile == WasmEntryPointProfile.Process)
        {
            exports.Insert(0, new("run", entryPointIndex));
        }
        AddOrdered(exports, requestedExports);
        AddOrdered(exports, hostCallbacks);
        AddOrdered(exports, asyncImports);
        AddOrdered(exports, asyncExportHelpers);
        return exports;
    }

    private static void AddOrdered<TCollection>(
        TCollection exports,
        IReadOnlyDictionary<string, int> indices)
        where TCollection : ICollection<WasmExport>
    {
        foreach ((var name, var index) in indices.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            exports.Add(new WasmExport(name, index));
        }
    }
}
