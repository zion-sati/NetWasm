using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Support;

internal sealed class AddressInstructionEmitter(ITargetLayout layouts) :
    IAddressInstructionEmitter
{
    public void Emit(IWasmInstructionWriter code, int constant)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(constant)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(constant)));
        }
    }

    public void Emit(IWasmInstructionWriter code, AddressOperation operation)
    {
        switch (operation)
        {
            case AddressOperation.Add when layouts.Target.UsesMemory64:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
                break;
            case AddressOperation.Add:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
                break;
            case AddressOperation.EqualZero when layouts.Target.UsesMemory64:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
                break;
            case AddressOperation.EqualZero:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            case AddressOperation.Equal when layouts.Target.UsesMemory64:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
                break;
            case AddressOperation.Equal:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
