using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSImportSetAppender
{
    void Append(IList<WasmFunctionDefinition> functions, int importCount,
        ImmutableDictionary<string, int>.Builder indices,
        WasmEmissionRequest request, IFunctionIndexResolver functionIndices,
        IList<ManagedBoundaryPlanEntry> boundaryEntries);
}
