using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class StackTraceFrameEntryEmitter(
    IRuntimeImportResolver runtimeImports) : IStackTraceFrameEntryEmitter
{
    public void Enter(
        IWasmInstructionWriter code,
        int methodId,
        RuntimeImportSelection runtimeImportSelection)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(methodId);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(methodId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.StackTraceFrameEnter,
                runtimeImportSelection))));
    }
}
