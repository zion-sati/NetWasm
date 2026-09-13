using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IModuleImportCollector
{
    ImmutableArray<WasmFunctionImport> Collect(ModuleImportCollectionRequest request);
}
