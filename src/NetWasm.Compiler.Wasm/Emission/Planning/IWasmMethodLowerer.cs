using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IWasmMethodLowerer
{
    StructuredMethod Lower(ManagedMethodBody method);
}
