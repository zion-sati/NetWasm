using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSImportHelperAppender
{
    void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        ImmutableDictionary<string, int>.Builder indices,
        JavaScriptAsyncMethodBinding binding,
        IFunctionIndexResolver functionIndices,
        IList<ManagedBoundaryPlanEntry> boundaryEntries);
}
