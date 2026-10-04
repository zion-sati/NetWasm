using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class StackTraceFrameLocationEmitter(
    IRuntimeImportResolver runtimeImports) : IStackTraceFrameLocationEmitter
{
    public void Update(
        IWasmInstructionWriter code,
        int methodId,
        int symbolId,
        RuntimeImportSelection runtimeImportSelection)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(methodId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(symbolId);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(methodId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(symbolId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.StackTraceFrameLocation,
                runtimeImportSelection))));
    }
}
