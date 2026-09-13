using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class DelegateEqualityFunctionEmitter(
    ITargetLayout layouts,
    IReferenceComparisonEmitter references,
    IDelegateLeafEqualityEmitter leafEquality,
    IGeneratedFunctionWriterFactory writers) : IDelegateEqualityFunctionEmitter
{
    public byte[] Emit(DelegateHelperTarget target)
    {
        const int count = 2;
        const int index = 3;
        const int leftLeaf = 4;
        const int rightLeaf = 5;
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [CliValueKind.I4, CliValueKind.I4,
             CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            layouts.Target);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        references.Emit(code.Instructions, ReferenceComparison.Equal);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Count.Value)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)count)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)count)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Count.Value)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Loop, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)index)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)count)));
        code.Instructions.Write(WasmInstruction.NoOperand(
            WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)index)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Leaf.Value)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)leftLeaf)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)index)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Leaf.Value)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)rightLeaf)));
        leafEquality.Emit(code, leftLeaf, rightLeaf);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)index)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)index)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
