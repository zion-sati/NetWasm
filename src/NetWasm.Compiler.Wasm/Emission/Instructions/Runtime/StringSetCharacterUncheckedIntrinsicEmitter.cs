using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class StringSetCharacterUncheckedIntrinsicEmitter(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.ManagedReference)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Local(1, CliValueKind.I4)))));
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(sizeof(char))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(sizeof(char))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Local(2, CliValueKind.I4)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store16, WasmInstructionOperand.Memory(1, (uint)(objects.StringDataOffset))));
    }
}
