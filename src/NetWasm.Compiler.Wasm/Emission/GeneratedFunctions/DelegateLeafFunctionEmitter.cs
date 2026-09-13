using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class DelegateLeafFunctionEmitter(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects,
    IReferenceComparisonEmitter references,
    IAddressInstructionEmitter addresses,
    IGeneratedFunctionWriterFactory writers) : IDelegateLeafFunctionEmitter
{
    public byte[] Emit(DelegateHelperTarget target)
    {
        const int leftCount = 2;
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(code.Bytes, [CliValueKind.I4], layouts.Target);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        addresses.Emit(code.Instructions, 0);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        ManagedMemoryEmitter.EmitLoadBySize(
            code.Instructions, layouts.Target, objects.DelegateLeftOffset,
            layouts.Target.ObjectReferenceSize);
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        ManagedMemoryEmitter.EmitLoadBySize(
            code.Instructions, layouts.Target, objects.DelegateLeftOffset,
            layouts.Target.ObjectReferenceSize);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Count.Value)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(leftCount)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(leftCount)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        ManagedMemoryEmitter.EmitLoadBySize(
            code.Instructions, layouts.Target, objects.DelegateLeftOffset,
            layouts.Target.ObjectReferenceSize);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Leaf.Value)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        ManagedMemoryEmitter.EmitLoadBySize(
            code.Instructions, layouts.Target, objects.DelegateRightOffset,
            layouts.Target.ObjectReferenceSize);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(leftCount)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Leaf.Value)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
