using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class DelegateLeafEqualityEmitter(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects,
    IReferenceComparisonEmitter references) : IDelegateLeafEqualityEmitter
{
    public void Emit(GeneratedFunctionWriterLease code, int leftLocal, int rightLocal)
    {
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)leftLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)rightLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)leftLocal)));
        ManagedMemoryEmitter.EmitLoadBySize(
            code.Instructions,
            layouts.Target,
            objects.DelegateTargetOffset,
            layouts.Target.ObjectReferenceSize);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)rightLocal)));
        ManagedMemoryEmitter.EmitLoadBySize(
            code.Instructions,
            layouts.Target,
            objects.DelegateTargetOffset,
            layouts.Target.ObjectReferenceSize);
        references.Emit(code.Instructions, ReferenceComparison.Equal);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)leftLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.DelegateMethodIdOffset)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)rightLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.DelegateMethodIdOffset)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
    }
}
