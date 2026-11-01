using System.Collections.Frozen;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface ICollectingRuntimeImportResolver
{
    // The runtime functions that can collect, by the index a call to one names
    // in a module with the selected imports.
    FrozenDictionary<uint, RuntimeImportSymbol> Resolve(RuntimeImportSelection selection);
}
