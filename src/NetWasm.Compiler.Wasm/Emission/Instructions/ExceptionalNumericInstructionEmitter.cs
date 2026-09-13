using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class ExceptionalNumericInstructionEmitter(
    ITargetLayout layouts,
    ICheckedBinaryEmitter checkedBinary,
    IImplicitExceptionEmitter exceptions) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.AddChecked, EmitCheckedBinary),
        Command(CilOperation.AddCheckedUnsigned, EmitCheckedBinary),
        Command(CilOperation.SubtractChecked, EmitCheckedBinary),
        Command(CilOperation.SubtractCheckedUnsigned, EmitCheckedBinary),
        Command(CilOperation.MultiplyChecked, EmitCheckedBinary),
        Command(CilOperation.MultiplyCheckedUnsigned, EmitCheckedBinary),
        Command(CilOperation.Divide, (request, code) => EmitDivision(request, code, false)),
        Command(CilOperation.DivideUnsigned, (request, code) => EmitDivision(request, code, true)),
        Command(CilOperation.Remainder, (request, code) => EmitRemainder(request, code, false)),
        Command(CilOperation.RemainderUnsigned, (request, code) => EmitRemainder(request, code, true)),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) =>
        new(operation, InstructionFamily.Numeric, emit);

    private void EmitCheckedBinary(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var left = request.Stack.Count - 2;
        var type = NumericStackTypes.GetCompatible(
            request.Stack[left], request.Stack[left + 1]);
        var unsigned = request.Instruction.Operation is
            CilOperation.AddCheckedUnsigned or CilOperation.SubtractCheckedUnsigned or
            CilOperation.MultiplyCheckedUnsigned;
        NormalizeCheckedOperand(request, code, left, type, unsigned);
        NormalizeCheckedOperand(request, code, left + 1, type, unsigned);
        var leftLocal = GetStackLocal(request.Context, left, type);
        var rightLocal = GetStackLocal(request.Context, left + 1, type);
        var usesI64 = type == CliValueKind.I8 ||
                      type == CliValueKind.NativeInt && layouts.Target.UsesMemory64;
        var resultLocal = usesI64
            ? request.Context.NumericTemporaryI8
            : request.Context.NumericTemporaryI4;
        checkedBinary.Emit(
            code,
            request.Instruction.Operation,
            type,
            leftLocal,
            rightLocal,
            resultLocal);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(resultLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(leftLocal))));
        request.Stack.RemoveAt(left + 1);
        request.Stack[left] = type;
    }

    private void NormalizeCheckedOperand(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int slot,
        CliValueKind operationType,
        bool unsigned)
    {
        var sourceType = request.Stack[slot];
        if (sourceType == operationType)
        {
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(request.Context, slot, sourceType))));
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(unsigned
                ? WasmOpcodes.I64ExtendI32Unsigned
                : WasmOpcodes.I64ExtendI32Signed));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(request.Context, slot, operationType))));
    }

    private void EmitDivision(InstructionEmissionRequest request, IWasmInstructionWriter code, bool unsigned)
    {
        var left = request.Stack.Count - 2;
        var type = NumericStackTypes.GetCompatible(
            request.Stack[left],
            request.Stack[left + 1]);
        if (type is CliValueKind.F4 or CliValueKind.F8)
        {
            EmitBinary(
                request, code, WasmInstruction.NoOperand(WasmOpcodes.I32DivideSigned),
                WasmInstruction.NoOperand(WasmOpcodes.I64DivideSigned),
                WasmInstruction.NoOperand(WasmOpcodes.F32Divide),
                WasmInstruction.NoOperand(WasmOpcodes.F64Divide));
            return;
        }
        EmitIntegerZeroGuard(request, code, left + 1);
        if (!unsigned)
        {
            EmitSignedDivisionOverflowGuard(request, code, left, type);
        }
        EmitBinary(
            request, code, unsigned
                ? WasmInstruction.NoOperand(WasmOpcodes.I32DivideUnsigned)
                : WasmInstruction.NoOperand(WasmOpcodes.I32DivideSigned),
            unsigned
                ? WasmInstruction.NoOperand(WasmOpcodes.I64DivideUnsigned)
                : WasmInstruction.NoOperand(WasmOpcodes.I64DivideSigned),
            WasmInstruction.NoOperand(WasmOpcodes.F32Divide),
            WasmInstruction.NoOperand(WasmOpcodes.F64Divide));
    }

    private void EmitRemainder(InstructionEmissionRequest request, IWasmInstructionWriter code, bool unsigned)
    {
        var left = request.Stack.Count - 2;
        var type = NumericStackTypes.GetCompatible(
            request.Stack[left],
            request.Stack[left + 1]);
        if (type is CliValueKind.F4 or CliValueKind.F8)
        {
            EmitFloatingRemainder(request, code, type);
            return;
        }
        EmitIntegerZeroGuard(request, code, left + 1);
        EmitBinary(
            request, code, unsigned
                ? WasmInstruction.NoOperand(WasmOpcodes.I32RemainderUnsigned)
                : WasmInstruction.NoOperand(WasmOpcodes.I32RemainderSigned),
            unsigned
                ? WasmInstruction.NoOperand(WasmOpcodes.I64RemainderUnsigned)
                : WasmInstruction.NoOperand(WasmOpcodes.I64RemainderSigned),
            WasmInstruction.NoOperand(WasmOpcodes.F32Divide),
            WasmInstruction.NoOperand(WasmOpcodes.F64Divide));
    }

    private void EmitBinary(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        WasmInstruction i32Operation,
        WasmInstruction i64Operation,
        WasmInstruction f32Operation,
        WasmInstruction f64Operation)
    {
        var left = request.Stack.Count - 2;
        var leftType = request.Stack[left];
        var rightType = request.Stack[left + 1];
        var type = NumericStackTypes.GetCompatible(leftType, rightType);
        EmitOperand(request, code, left, leftType, type);
        EmitOperand(request, code, left + 1, rightType, type);
        if (type == CliValueKind.I4) code.Write(i32Operation);
        else if (type == CliValueKind.I8) code.Write(i64Operation);
        else if (type == CliValueKind.NativeInt)
        {
            if (layouts.Target.UsesMemory64) code.Write(i64Operation);
            else code.Write(i32Operation);
        }
        else if (type == CliValueKind.F4) code.Write(f32Operation);
        else code.Write(f64Operation);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, left, type)))));
        request.Stack.RemoveAt(left + 1);
        request.Stack[left] = type;
    }

    private void EmitOperand(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int slot,
        CliValueKind source,
        CliValueKind operationType)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, source)))));
        if (layouts.Target.UsesMemory64 &&
            operationType == CliValueKind.NativeInt && source == CliValueKind.I4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Signed));
        }
    }

    private void EmitIntegerZeroGuard(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int slot)
    {
        var type = request.Stack[slot];
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, type)))));
        if (type == CliValueKind.I8 ||
            type == CliValueKind.NativeInt && layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
        }
        else
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.DivideByZero);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitSignedDivisionOverflowGuard(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int left,
        CliValueKind type)
    {
        var usesI64 = type == CliValueKind.I8 ||
                      type == CliValueKind.NativeInt && layouts.Target.UsesMemory64;
        EmitOperand(request, code, left, request.Stack[left], type);
        if (usesI64)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(long.MinValue)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(int.MinValue)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        }
        EmitOperand(request, code, left + 1, request.Stack[left + 1], type);
        if (usesI64)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(-1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(-1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Overflow);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitFloatingRemainder(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        CliValueKind type)
    {
        var left = request.Stack.Count - 2;
        var leftLocal = GetStackLocal(request.Context, left, type);
        var rightLocal = GetStackLocal(request.Context, left + 1, type);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(leftLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(leftLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(rightLocal))));
        if (type == CliValueKind.F4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Divide));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Truncate));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(rightLocal))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Subtract));
        }
        else
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Divide));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Truncate));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(rightLocal))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Subtract));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(leftLocal))));
        request.Stack.RemoveAt(left + 1);
        request.Stack[left] = type;
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
