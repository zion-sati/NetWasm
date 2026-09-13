using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IDelegateFunctionAppender
{
    void Append(IList<WasmFunctionDefinition> functions,
        ImmutableArray<MethodInstanceModel> invokes,
        ImmutableArray<CliTypeIdentity> delegateTypes,
        OptionalFunctionIndex countIndex, OptionalFunctionIndex leafIndex,
        OptionalFunctionIndex equalityIndex, DelegateInvokeTarget invokeTarget,
        IFunctionIndexResolver functionIndices);
}
