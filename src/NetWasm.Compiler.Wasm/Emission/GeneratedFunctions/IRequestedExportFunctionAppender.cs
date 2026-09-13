using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IRequestedExportFunctionAppender
{
    void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<string, int> requestedExportIndices,
        IDictionary<string, int> asyncHelperIndices,
        string exportName,
        EntityKey methodKey,
        IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding> asyncBindings,
        RuntimeInitializationPlan initialization,
        bool hasFinalizers,
        WasmModuleProfile profile,
        IFunctionIndexResolver functionIndices,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries);
}
