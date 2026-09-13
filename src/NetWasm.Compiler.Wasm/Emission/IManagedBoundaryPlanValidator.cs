using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission;

public interface IManagedBoundaryPlanValidator
{
    void Validate(
        IReadOnlyList<WasmFunctionDefinition> functions,
        IReadOnlyList<WasmExport> exports,
        int importedFunctionCount,
        IEnumerable<ManagedBoundaryPlanEntry> entries);
}
