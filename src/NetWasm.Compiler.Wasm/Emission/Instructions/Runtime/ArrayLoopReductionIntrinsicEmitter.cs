using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// ArrayLoops.Xor, Or and And fold count elements eight bytes at a time into
// two 64-bit accumulators, 16 bytes a pass, then fold the tail of 8, 4, 2 and
// 1 bytes, the accumulators into one, and the accumulator's lanes down to one
// element, which is widened as the element type's sign says. Every chunk starts
// a whole number of elements in, so its lanes are whole elements; a narrow
// tail is padded with the operation's identity.
internal sealed class ArrayLoopReductionIntrinsicEmitter(IValueLayoutProvider values) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var (combine, identity) = request.Intrinsic switch
        {
            RuntimeIntrinsic.ArrayLoopXor => (WasmOpcodes.I64Xor, 0L),
            RuntimeIntrinsic.ArrayLoopOr => (WasmOpcodes.I64Or, 0L),
            RuntimeIntrinsic.ArrayLoopAnd => (WasmOpcodes.I64And, -1L),
            _ => throw new InvalidOperationException($"{request.Intrinsic} is not an array loop reduction."),
        };
        var element = request.Method.MethodArguments[0];
        var size = values.GetValueLayout(element).Size;
        var target = request.Target;
        var data = request.Local(0, CliValueKind.NativeInt);
        var count = request.Local(1, CliValueKind.I4);
        // Count's slot, and two slots above the arguments, which nothing else holds.
        var bytes = request.Local(1, CliValueKind.NativeInt);
        var first = request.Local(2, CliValueKind.I8);
        var second = request.Local(3, CliValueKind.I8);

        Get(code, count);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(size)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
        if (target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        Set(code, bytes);
        foreach (var accumulator in (ReadOnlySpan<int>)[first, second])
        {
            Constant64(code, identity);
            Set(code, accumulator);
        }

        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Loop, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Get(code, bytes);
        Address(code, target, 16);
        code.Write(WasmInstruction.NoOperand(target.UsesMemory64 ? WasmOpcodes.I64LessThanUnsigned : WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf, WasmInstructionOperand.Unsigned(1)));
        foreach (var (accumulator, offset) in (ReadOnlySpan<(int, uint)>)[(first, 0u), (second, 8u)])
        {
            Get(code, accumulator);
            Get(code, data);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Load, WasmInstructionOperand.Memory(0, offset)));
            code.Write(WasmInstruction.NoOperand(combine));
            Set(code, accumulator);
        }
        Advance(code, target, data, bytes, 16);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        Get(code, first);
        Get(code, second);
        code.Write(WasmInstruction.NoOperand(combine));
        Set(code, first);

        // Fewer than 16 bytes remain, a whole number of elements.
        foreach (var width in (ReadOnlySpan<int>)[8, 4, 2, 1])
        {
            if (width < size) continue;
            Get(code, bytes);
            Address(code, target, width);
            code.Write(WasmInstruction.NoOperand(
                target.UsesMemory64 ? WasmOpcodes.I64GreaterThanOrEqualUnsigned : WasmOpcodes.I32GreaterThanOrEqualUnsigned));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            Get(code, first);
            Get(code, data);
            if (width == 8)
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Load, WasmInstructionOperand.Memory(0, 0)));
            }
            else
            {
                code.Write(WasmInstruction.WithOperand(width switch
                {
                    4 => WasmOpcodes.I32Load,
                    2 => WasmOpcodes.I32Load16Unsigned,
                    _ => WasmOpcodes.I32Load8Unsigned,
                }, WasmInstructionOperand.Memory(0, 0)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
                if (identity != 0)
                {
                    Constant64(code, identity << (width * 8));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Or));
                }
            }
            code.Write(WasmInstruction.NoOperand(combine));
            Set(code, first);
            Advance(code, target, data, bytes, width);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }

        for (var shift = 32; shift >= size * 8; shift /= 2)
        {
            Get(code, first);
            Get(code, first);
            Constant64(code, shift);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightUnsigned));
            code.Write(WasmInstruction.NoOperand(combine));
            Set(code, first);
        }

        Get(code, first);
        if (request.Method.Signature.ReturnType == CliValueKind.I8)
        {
            Set(code, request.Local(0, CliValueKind.I8));
            return;
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        var signed = element.CanonicalName is "primitive:i1" or "primitive:i2";
        switch (size)
        {
            case 1 when signed:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Extend8Signed));
                break;
            case 2 when signed:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Extend16Signed));
                break;
            case 1 or 2:
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(size == 1 ? 0xff : 0xffff)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
                break;
        }
        Set(code, request.Local(0, CliValueKind.I4));
    }

    // Moves the address on and the remaining byte count down by `width`.
    private static void Advance(IWasmInstructionWriter code, WasmTargetLayout target, int data, int bytes, int width)
    {
        Get(code, data);
        Address(code, target, width);
        code.Write(WasmInstruction.NoOperand(target.UsesMemory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
        Set(code, data);
        Get(code, bytes);
        Address(code, target, width);
        code.Write(WasmInstruction.NoOperand(target.UsesMemory64 ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract));
        Set(code, bytes);
    }

    private static void Address(IWasmInstructionWriter code, WasmTargetLayout target, int value) =>
        code.Write(target.UsesMemory64
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));

    private static void Constant64(IWasmInstructionWriter code, long value) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
