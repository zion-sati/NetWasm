using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class MethodFrameExitEmitter(
    IRuntimeImportResolver runtimeImports,
    IStackTraceFrameExitEmitter stackTraceFrames) : IMethodFrameExitEmitter
{
    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);

        if (context.FilterEnvironment.RootSlotCount != 0)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)context.FilterRootFrame)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave))));
        }
        if (context.ValueLayout.Size != 0)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave))));
        }
        if (context.RootSlotCount != 0)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave))));
        }
        if (context.StackTraceMethodId != 0)
        {
            stackTraceFrames.Leave(
                code,
                context.StackTraceMethodId,
                context.RuntimeImportSelection);
        }
    }
}
