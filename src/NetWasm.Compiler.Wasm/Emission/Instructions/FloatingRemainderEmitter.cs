using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

/// <summary>Emits exact truncating remainder through binary long division.</summary>
internal sealed class FloatingRemainderEmitter : IFloatingRemainderEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        CliValueKind type,
        int leftLocal,
        int rightLocal,
        int countLocal,
        int signLocal)
    {
        if (type is not (CliValueKind.F4 or CliValueKind.F8))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }
        var single = type == CliValueKind.F4;
        var absolute = single ? WasmOpcodes.F32Absolute : WasmOpcodes.F64Absolute;
        var less = single ? WasmOpcodes.F32LessThan : WasmOpcodes.F64LessThan;
        var greater = single ? WasmOpcodes.F32GreaterThan : WasmOpcodes.F64GreaterThan;
        var greaterOrEqual = single ? WasmOpcodes.F32GreaterThanOrEqual : WasmOpcodes.F64GreaterThanOrEqual;
        var multiply = single ? WasmOpcodes.F32Multiply : WasmOpcodes.F64Multiply;
        var subtract = single ? WasmOpcodes.F32Subtract : WasmOpcodes.F64Subtract;

        // These ordered comparisons also reject NaNs. Infinity is a valid divisor.
        Local(WasmOpcodes.LocalGet, leftLocal);
        Op(absolute);
        Constant(double.PositiveInfinity);
        Op(less);
        Local(WasmOpcodes.LocalGet, rightLocal);
        Op(absolute);
        Constant(0);
        Op(greater);
        Op(WasmOpcodes.I32And);
        Block(WasmOpcodes.If);

        // Preserve the original dividend when |x| < |y| (also x % infinity).
        Local(WasmOpcodes.LocalGet, rightLocal);
        Op(absolute);
        Local(WasmOpcodes.LocalSet, rightLocal);
        Local(WasmOpcodes.LocalGet, leftLocal);
        Op(absolute);
        Local(WasmOpcodes.LocalGet, rightLocal);
        Op(greaterOrEqual);
        Block(WasmOpcodes.If);

        Local(WasmOpcodes.LocalGet, leftLocal);
        if (single)
        {
            Op(WasmOpcodes.I32ReinterpretF32);
            Op(WasmOpcodes.I64ExtendI32Unsigned);
        }
        else
        {
            Op(WasmOpcodes.I64ReinterpretF64);
        }
        Local(WasmOpcodes.LocalSet, signLocal);
        Local(WasmOpcodes.LocalGet, leftLocal);
        Op(absolute);
        Local(WasmOpcodes.LocalSet, leftLocal);
        Integer(0);
        Local(WasmOpcodes.LocalSet, countLocal);

        // Scale without overflow. Rounding x/2 at the subnormal boundary can
        // introduce one extra doubling; the conditional subtraction handles it.
        Block(WasmOpcodes.Block);
        Block(WasmOpcodes.Loop);
        Local(WasmOpcodes.LocalGet, rightLocal);
        Local(WasmOpcodes.LocalGet, leftLocal);
        Constant(0.5);
        Op(multiply);
        Op(greater);
        Branch(WasmOpcodes.BranchIf, 1);
        Local(WasmOpcodes.LocalGet, rightLocal);
        Constant(2);
        Op(multiply);
        Local(WasmOpcodes.LocalSet, rightLocal);
        Local(WasmOpcodes.LocalGet, countLocal);
        Integer(1);
        Op(WasmOpcodes.I32Add);
        Local(WasmOpcodes.LocalSet, countLocal);
        Branch(WasmOpcodes.Branch, 0);
        Op(WasmOpcodes.End);
        Op(WasmOpcodes.End);

        // Each subtraction is exact: y <= x <= 2y (Sterbenz's lemma). Halving
        // retraces exact doublings, including subnormals, down to the original y.
        Block(WasmOpcodes.Block);
        Block(WasmOpcodes.Loop);
        Local(WasmOpcodes.LocalGet, leftLocal);
        Local(WasmOpcodes.LocalGet, rightLocal);
        Op(greaterOrEqual);
        Block(WasmOpcodes.If);
        Local(WasmOpcodes.LocalGet, leftLocal);
        Local(WasmOpcodes.LocalGet, rightLocal);
        Op(subtract);
        Local(WasmOpcodes.LocalSet, leftLocal);
        Op(WasmOpcodes.End);
        Local(WasmOpcodes.LocalGet, countLocal);
        Op(WasmOpcodes.I32EqualZero);
        Branch(WasmOpcodes.BranchIf, 1);
        Local(WasmOpcodes.LocalGet, rightLocal);
        Constant(0.5);
        Op(multiply);
        Local(WasmOpcodes.LocalSet, rightLocal);
        Local(WasmOpcodes.LocalGet, countLocal);
        Integer(1);
        Op(WasmOpcodes.I32Subtract);
        Local(WasmOpcodes.LocalSet, countLocal);
        Branch(WasmOpcodes.Branch, 0);
        Op(WasmOpcodes.End);
        Op(WasmOpcodes.End);

        Local(WasmOpcodes.LocalGet, leftLocal);
        Local(WasmOpcodes.LocalGet, signLocal);
        if (single)
        {
            Op(WasmOpcodes.I32WrapI64);
            Op(WasmOpcodes.F32ReinterpretI32);
        }
        else
        {
            Op(WasmOpcodes.F64ReinterpretI64);
        }
        Op(single ? WasmOpcodes.F32CopySign : WasmOpcodes.F64CopySign);
        Local(WasmOpcodes.LocalSet, leftLocal);
        Op(WasmOpcodes.End);
        Op(WasmOpcodes.Else);
        Constant(double.NaN);
        Local(WasmOpcodes.LocalSet, leftLocal);
        Op(WasmOpcodes.End);

        void Op(byte opcode) => code.Write(WasmInstruction.NoOperand(opcode));
        void Local(byte opcode, int local) =>
            code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)local)));
        void Branch(byte opcode, uint depth) =>
            code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned(depth)));
        void Block(byte opcode) => code.Write(WasmInstruction.WithOperand(
            opcode, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        void Integer(int value) => code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        void Constant(double value) => code.Write(single
            ? WasmInstruction.WithOperand(WasmOpcodes.F32Constant, WasmInstructionOperand.Float32((float)value))
            : WasmInstruction.WithOperand(WasmOpcodes.F64Constant, WasmInstructionOperand.Float64(value)));
    }
}
