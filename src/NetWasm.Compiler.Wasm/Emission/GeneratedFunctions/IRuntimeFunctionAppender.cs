using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IRuntimeFunctionAppender
{
    RuntimeFunctionAppendResult Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        ImmutableArray<FilterFunclet> filters,
        IReadOnlyDictionary<int, int> filterIndices,
        MethodDefinitionModel entryPoint,
        RuntimeInitializationPlan initialization,
        WasmEntryPointProfile entryPointProfile,
        JavaScriptAsyncMethodBinding? asyncBinding,
        IDictionary<string, int> asyncHelperIndices,
        IFunctionIndexResolver functionIndices,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries,
        EntityKey? entryPointArgumentFactory = null);
}
