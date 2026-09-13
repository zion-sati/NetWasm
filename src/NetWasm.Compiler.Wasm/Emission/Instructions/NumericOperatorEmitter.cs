using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class NumericOperatorEmitter(ITargetLayout layouts) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.Add, EmitAdd),
        Command(CilOperation.Subtract, EmitSubtract),
        Command(CilOperation.Multiply, EmitMultiply),
        Command(CilOperation.BitwiseAnd, EmitBitwiseAnd),
        Command(CilOperation.BitwiseOr, EmitBitwiseOr),
        Command(CilOperation.BitwiseXor, EmitBitwiseXor),
        Command(CilOperation.ShiftLeft, EmitShiftLeft),
        Command(CilOperation.ShiftRightSigned, EmitShiftRightSigned),
        Command(CilOperation.ShiftRightUnsigned, EmitShiftRightUnsigned),
        Command(CilOperation.Negate, EmitNegate),
        Command(CilOperation.OnesComplement, EmitOnesComplement),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) =>
        new(operation, InstructionFamily.Numeric, emit);

    private void EmitAdd(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitNumericBinary(
        request, code, WasmInstruction.NoOperand(WasmOpcodes.I32Add),
        WasmInstruction.NoOperand(WasmOpcodes.I64Add),
        WasmInstruction.NoOperand(WasmOpcodes.F32Add),
        WasmInstruction.NoOperand(WasmOpcodes.F64Add),
        NumericStackTypes.GetAddCompatible);

    private void EmitSubtract(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitNumericBinary(
        request, code, WasmInstruction.NoOperand(WasmOpcodes.I32Subtract),
        WasmInstruction.NoOperand(WasmOpcodes.I64Subtract),
        WasmInstruction.NoOperand(WasmOpcodes.F32Subtract),
        WasmInstruction.NoOperand(WasmOpcodes.F64Subtract),
        NumericStackTypes.GetSubtractCompatible);

    private void EmitMultiply(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitNumericBinary(
        request, code, WasmInstruction.NoOperand(WasmOpcodes.I32Multiply),
        WasmInstruction.NoOperand(WasmOpcodes.I64Multiply),
        WasmInstruction.NoOperand(WasmOpcodes.F32Multiply),
        WasmInstruction.NoOperand(WasmOpcodes.F64Multiply));

    private void EmitBitwiseAnd(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitIntegerBinary(
            request, code, WasmInstruction.NoOperand(WasmOpcodes.I32And),
            WasmInstruction.NoOperand(WasmOpcodes.I64And));

    private void EmitBitwiseOr(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitIntegerBinary(
            request, code, WasmInstruction.NoOperand(WasmOpcodes.I32Or),
            WasmInstruction.NoOperand(WasmOpcodes.I64Or));

    private void EmitBitwiseXor(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitIntegerBinary(
            request, code, WasmInstruction.NoOperand(WasmOpcodes.I32Xor),
            WasmInstruction.NoOperand(WasmOpcodes.I64Xor));

    private void EmitShiftLeft(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitIntegerShift(
            request, code, WasmInstruction.NoOperand(WasmOpcodes.I32ShiftLeft),
            WasmInstruction.NoOperand(WasmOpcodes.I64ShiftLeft));

    private void EmitShiftRightSigned(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitIntegerShift(
            request, code, WasmInstruction.NoOperand(WasmOpcodes.I32ShiftRightSigned),
            WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightSigned));

    private void EmitShiftRightUnsigned(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitIntegerShift(
            request, code, WasmInstruction.NoOperand(WasmOpcodes.I32ShiftRightUnsigned),
            WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightUnsigned));

    private void EmitNegate(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitUnary(request, code, true);

    private void EmitOnesComplement(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitUnary(request, code, false);

    private void EmitNumericBinary(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        WasmInstruction i32Operation,
        WasmInstruction i64Operation,
        WasmInstruction f32Operation,
        WasmInstruction f64Operation,
        Func<CliValueKind, CliValueKind, CliValueKind>? resolveResult = null)
    {
        var left = request.Stack.Count - 2;
        var leftType = request.Stack[left];
        var rightType = request.Stack[left + 1];
        var resultType = (resolveResult ?? NumericStackTypes.GetCompatible)(leftType, rightType);
        var operationType = resultType == CliValueKind.ManagedAddress
            ? CliValueKind.NativeInt
            : resultType;
        EmitOperand(request, code, left, leftType, operationType);
        EmitOperand(request, code, left + 1, rightType, operationType);
        EmitForType(
            request,
            code,
            operationType,
            i32Operation,
            i64Operation,
            f32Operation,
            f64Operation);
        StoreBinaryResult(request, code, left, resultType);
    }

    private void EmitIntegerBinary(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        WasmInstruction i32Operation,
        WasmInstruction i64Operation)
    {
        var left = request.Stack.Count - 2;
        var leftType = request.Stack[left];
        var rightType = request.Stack[left + 1];
        var type = NumericStackTypes.GetCompatible(leftType, rightType);
        EmitOperand(request, code, left, leftType, type);
        EmitOperand(request, code, left + 1, rightType, type);
        if (type == CliValueKind.I8 ||
            type == CliValueKind.NativeInt && layouts.Target.UsesMemory64)
        {
            code.Write(i64Operation);
        }
        else if (type is CliValueKind.I4 or CliValueKind.NativeInt)
        {
            code.Write(i32Operation);
        }
        else
        {
            throw new InvalidOperationException(
                "bitwise operation requires an integer stack type");
        }
        StoreBinaryResult(request, code, left, type);
    }

    private void EmitIntegerShift(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        WasmInstruction i32Operation,
        WasmInstruction i64Operation)
    {
        var left = request.Stack.Count - 2;
        var valueType = request.Stack[left];
        var shiftType = request.Stack[left + 1];
        var usesI64 = valueType == CliValueKind.I8 ||
                      valueType == CliValueKind.NativeInt &&
                      layouts.Target.UsesMemory64;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, left, valueType)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, left + 1, shiftType)))));
        if (usesI64 && shiftType != CliValueKind.I8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        if (usesI64) code.Write(i64Operation);
        else code.Write(i32Operation);
        StoreBinaryResult(request, code, left, valueType);
    }

    private void EmitUnary(InstructionEmissionRequest request, IWasmInstructionWriter code, bool negate)
    {
        var slot = request.Stack.Count - 1;
        var type = request.Stack[slot];
        if (type is CliValueKind.F4 or CliValueKind.F8)
        {
            if (!negate)
            {
                throw new InvalidOperationException(
                    "ones complement requires an integer operand");
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, type)))));
            if (type == CliValueKind.F4) code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Negate));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Negate));
        }
        else
        {
            var usesI64 = type == CliValueKind.I8 ||
                          type == CliValueKind.NativeInt &&
                          layouts.Target.UsesMemory64;
            if (negate)
            {
                if (usesI64) code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0)));
                else code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, type)))));
                if (usesI64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Subtract));
                else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
            }
            else
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, type)))));
                if (usesI64)
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(-1)));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Xor));
                }
                else
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(-1)));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Xor));
                }
            }
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, type)))));
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

    private void EmitForType(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        CliValueKind type,
        WasmInstruction i32Operation,
        WasmInstruction i64Operation,
        WasmInstruction f32Operation,
        WasmInstruction f64Operation)
    {
        var operations = new Dictionary<CliValueKind, WasmInstruction>
        {
            [CliValueKind.I4] = i32Operation,
            [CliValueKind.I8] = i64Operation,
            [CliValueKind.NativeInt] = layouts.Target.UsesMemory64
                ? i64Operation
                : i32Operation,
            [CliValueKind.F4] = f32Operation,
            [CliValueKind.F8] = f64Operation,
        };
        code.Write(operations[type]);
    }

    private void StoreBinaryResult(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int left,
        CliValueKind type)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, left, type)))));
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
