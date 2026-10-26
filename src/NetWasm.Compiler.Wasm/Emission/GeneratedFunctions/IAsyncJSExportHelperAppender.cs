using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSExportHelperAppender
{
    void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<string, int> indices,
        JavaScriptAsyncMethodBinding binding,
        ManagedAsyncBoundaryNames names,
        ManagedAsyncBoundaryKinds kinds,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries,
        AsyncTaskCompletionPlan completion,
        InteropImportPlan? interopImports = null);
}
