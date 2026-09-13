using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IHostCallbackSetAppender
{
    void Append(IList<WasmFunctionDefinition> functions, int importCount,
        IDictionary<string, int> callbackIndices,
        ImmutableArray<HostCallbackDeclaration> callbacks,
        FunctionIndexMap functionIndices, RuntimeInitializationPlan initialization,
        InteropImportPlan interopImports,
        RuntimeImportSelection runtimeImportSelection,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries);
}
