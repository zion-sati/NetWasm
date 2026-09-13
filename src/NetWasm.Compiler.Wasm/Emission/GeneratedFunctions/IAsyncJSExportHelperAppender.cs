using System.Collections.Generic;
using NetWasm.Compiler.Core;

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
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries);
}
