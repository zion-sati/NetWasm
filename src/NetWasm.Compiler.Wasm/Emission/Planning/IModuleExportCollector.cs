using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IModuleExportCollector
{
    IReadOnlyList<WasmExport> Collect(
        WasmEntryPointProfile entryPointProfile,
        int entryPointIndex,
        int filterDispatcherIndex,
        int finalizerDispatcherIndex,
        IReadOnlyDictionary<string, int> requestedExports,
        IReadOnlyDictionary<string, int> hostCallbacks,
        IReadOnlyDictionary<string, int> asyncImports,
        IReadOnlyDictionary<string, int> asyncExportHelpers);
}
