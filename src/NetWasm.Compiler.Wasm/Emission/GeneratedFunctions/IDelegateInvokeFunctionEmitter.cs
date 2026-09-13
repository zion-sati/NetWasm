using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IDelegateInvokeFunctionEmitter
{
    byte[] Emit(
        MethodInstanceModel invoke,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices);
}
