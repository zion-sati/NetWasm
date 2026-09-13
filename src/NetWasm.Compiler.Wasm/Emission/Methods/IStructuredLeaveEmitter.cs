using NetWasm.Compiler.Core;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IStructuredLeaveEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CilInstruction instruction,
        StructuredContinuationId? continuation,
        MethodEmissionContext context);
}
