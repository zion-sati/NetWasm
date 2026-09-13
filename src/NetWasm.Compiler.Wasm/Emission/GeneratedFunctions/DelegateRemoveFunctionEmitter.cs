using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class DelegateRemoveFunctionEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider types,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IReferenceComparisonEmitter references,
    IDelegateLeafEqualityEmitter leafEquality,
    IAddressInstructionEmitter addresses,
    IGeneratedFunctionWriterFactory writers) : IDelegateRemoveFunctionEmitter
{
    public byte[] Emit(DelegateHelperTarget target)
    {
        const int sourceCount = 2;
        const int valueCount = 3;
        const int candidate = 4;
        const int offset = 5;
        const int matches = 6;
        const int sourceLeaf = 7;
        const int valueLeaf = 8;
        const int result = 9;
        const int node = 10;
        const int rootFrame = 11;
        var delegateSize = target.DelegateTypes
            .Select(type => types.GetObjectLayout(type).Size)
            .Distinct()
            .Single();
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [CliValueKind.I4, CliValueKind.I4, CliValueKind.I4,
             CliValueKind.I4, CliValueKind.I4,
             CliValueKind.ManagedReference, CliValueKind.ManagedReference,
             CliValueKind.ManagedReference, CliValueKind.ManagedReference,
             CliValueKind.ManagedAddress],
            layouts.Target);

        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code.Instructions, ManagedExceptionKind.Argument);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Count.Value)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)sourceCount)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Count.Value)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)valueCount)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)valueCount)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceCount)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceCount)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)valueCount)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)offset)));

        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Loop, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)matches)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Loop, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)valueCount)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)offset)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Leaf.Value)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)sourceLeaf)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Leaf.Value)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)valueLeaf)));
        leafEquality.Emit(code, sourceLeaf, valueLeaf);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)matches)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(2)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)matches)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)offset)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)offset)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)offset)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)matches)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(3)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RootFrameEnter))));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)rootFrame)));
        ManagedMemoryEmitter.EmitRootSlotStore(code.Instructions, layouts.Target, rootFrame, 0, 0);
        ManagedMemoryEmitter.EmitRootSlotStore(code.Instructions, layouts.Target, rootFrame, 1, 1);
        addresses.Emit(code.Instructions, 0);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Loop, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceCount)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)offset)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)offset)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)valueCount)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)target.Indices.Leaf.Value)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)sourceLeaf)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)result)));
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceLeaf)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        ManagedMemoryEmitter.EmitRootSlotStore(code.Instructions, layouts.Target, rootFrame, 2, result);
        addresses.Emit(code.Instructions, delegateSize);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.Allocate))));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)node)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)node)));
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)rootFrame)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RootFrameLeave))));
        exceptions.Emit(code.Instructions, ManagedExceptionKind.OutOfMemory);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)node)));
        addresses.Emit(code.Instructions, 0);
        ManagedMemoryEmitter.EmitStoreBySize(
            code.Instructions, layouts.Target, objects.DelegateTargetOffset,
            layouts.Target.ObjectReferenceSize);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)node)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, (uint)objects.DelegateMethodIdOffset)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)node)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)result)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code.Instructions, layouts.Target, objects.DelegateLeftOffset,
            layouts.Target.ObjectReferenceSize);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)node)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceLeaf)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code.Instructions, layouts.Target, objects.DelegateRightOffset,
            layouts.Target.ObjectReferenceSize);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)node)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        ManagedMemoryEmitter.EmitRootSlotStore(code.Instructions, layouts.Target, rootFrame, 2, result);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)candidate)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)rootFrame)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RootFrameLeave))));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)result)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
