using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ValueFrameAddressEmitter(ITargetLayout layouts) :
    IValueFrameAddressEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        FilterCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        Emit(code, context, capture.Offset);
    }

    public void Emit(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        int offset)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
        if (offset == 0)
        {
            return;
        }
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(offset)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(offset)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
    }
}
