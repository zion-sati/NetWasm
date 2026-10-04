using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IModuleExportValidator
{
    void Validate(
        IReadOnlyCollection<WasmExport> exports,
        IReadOnlyDictionary<string, int> nativeCallbacks);
}
