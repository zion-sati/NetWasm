using System.Collections.Generic;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFilterFunctionAppender
{
    void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<int, int> indices,
        FilterFunclet filter,
        FilterEnvironmentLayout environment,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        IList<ManagedMethodEmissionRecord> managedMethodEmissions);
}
