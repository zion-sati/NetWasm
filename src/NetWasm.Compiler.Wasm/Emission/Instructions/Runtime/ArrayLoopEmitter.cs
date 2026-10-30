using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// The shape ArrayLoops.Fill and ArrayLoops.Copy share. With every array not
// null, end is clamped to each array's length; when start then lies in
// [0, end) the work runs on the byte count from start's elements and the
// result is end, and otherwise the result is start, so the loop's next
// iteration throws as it would have.
internal static class ArrayLoopEmitter
{
    public static void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        IRuntimeObjectLayout objects,
        ReadOnlySpan<int> arrays,
        int start,
        int end,
        Action work)
    {
        var target = request.Target;
        var result = request.Local(0, CliValueKind.I4);
        for (var index = 0; index < arrays.Length; index++)
        {
            Get(code, arrays[index]);
            code.Write(WasmInstruction.NoOperand(target.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
            if (index > 0) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Get(code, start);
        Set(code, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        foreach (var array in arrays)
        {
            Length(code, objects, array);
            Get(code, end);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            Length(code, objects, array);
            Set(code, end);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        // 0 <= start && start < end
        Get(code, start);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        Get(code, start);
        Get(code, end);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        work();
        Get(code, end);
        Set(code, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        Get(code, start);
        Set(code, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // Sets `local` to the address of the array's element at start.
    public static void ElementAddress(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int array,
        int start,
        int elementSize,
        int local)
    {
        ManagedMemoryEmitter.EmitArrayElementAddress(code, target, array, start, elementSize);
        Set(code, local);
    }

    // Sets `local` to (end - start) elements in bytes, pointer-sized.
    public static void ByteCount(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int start,
        int end,
        int elementSize,
        int local)
    {
        Get(code, end);
        Get(code, start);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(elementSize)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
        if (target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        Set(code, local);
    }

    private static void Length(IWasmInstructionWriter code, IRuntimeObjectLayout objects, int array)
    {
        Get(code, array);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)objects.ArrayLengthOffset)));
    }

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
