using System.Collections.Generic;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface INativeCallbackFunctionAppender
{
    void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<string, int> indices,
        NativeCallbackPlan callbacks,
        ModuleDataPlan moduleData,
        RuntimeImportSelection runtimeImportSelection,
        IFunctionIndexResolver functionIndices,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries);
}
