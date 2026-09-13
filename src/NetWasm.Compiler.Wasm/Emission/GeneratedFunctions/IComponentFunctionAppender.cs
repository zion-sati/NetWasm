using System.Collections.Immutable;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IComponentFunctionAppender
{
    ImmutableArray<string> Append(
        IList<WasmFunctionDefinition> functions,
        ICollection<WasmExport> exports,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries,
        WasmEmissionRequest request,
        WasmTarget target,
        RuntimeInitializationPlan initialization,
        int importCount,
        IReadOnlyDictionary<string, int> requestedExportIndices,
        IFunctionIndexResolver functionIndices);
}
