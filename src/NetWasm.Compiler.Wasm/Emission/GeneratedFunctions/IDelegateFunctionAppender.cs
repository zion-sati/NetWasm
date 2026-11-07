using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IDelegateFunctionAppender
{
    void Append(IList<WasmFunctionDefinition> functions,
        ImmutableArray<MethodInstanceModel> invokes,
        DelegateInvokeTarget invokeTarget,
        FunctionIndexMap indices,
        IFunctionIndexResolver functionIndices);
}
