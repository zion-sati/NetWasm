namespace NetWasm.Compiler.Wasm.Emission.Methods;

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Encoding;

internal interface IControlFlowDispatcherEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        ControlFlowDispatcherEmissionRequest request,
        Action<StructuredDispatcherBlock> emitBlock,
        Action<StructuredDispatcherBlock> emitCondition,
        Action<StructuredSequence> emitSequence);
}
