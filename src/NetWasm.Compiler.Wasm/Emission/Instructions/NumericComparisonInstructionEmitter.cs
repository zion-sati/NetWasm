using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class NumericComparisonInstructionEmitter(
    ITargetLayout layouts,
    IStackTypeCompatibilityValidator stackTypes) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.CompareEqual, (request, code) =>
            EmitComparison(request, code, ComparisonKind.Equal)),
        Command(CilOperation.CompareGreaterThanSigned, EmitGreaterThanSigned),
        Command(CilOperation.CompareGreaterThanUnsigned, (request, code) =>
            EmitComparison(request, code, ComparisonKind.GreaterThanUnsigned)),
        Command(CilOperation.CompareLessThanSigned, EmitLessThanSigned),
        Command(CilOperation.CompareLessThanUnsigned, (request, code) =>
            EmitComparison(request, code, ComparisonKind.LessThanUnsigned)),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) =>
        new(operation, InstructionFamily.Numeric, emit);

    private void EmitGreaterThanSigned(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitNumericComparison(
            request, code, () => code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanSigned)),
            () => code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanSigned)),
            () => code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32GreaterThan)),
            () => code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64GreaterThan)));

    private void EmitLessThanSigned(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitNumericComparison(
            request, code, () => code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned)),
            () => code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanSigned)),
            () => code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32LessThan)),
            () => code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64LessThan)));

    private void EmitNumericComparison(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        Action i32Operation,
        Action i64Operation,
        Action f32Operation,
        Action f64Operation)
    {
        var left = request.Stack.Count - 2;
        var type = NumericStackTypes.RequireMatching(request.Stack, left);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, left, type)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, left + 1, type)))));
        NumericComparisonEmitter.Emit(
            layouts.Target,
            type,
            i32Operation,
            i64Operation,
            f32Operation,
            f64Operation);
        StoreResult(request, code, left);
    }

    private void EmitComparison(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        ComparisonKind comparison)
    {
        var left = request.Stack.Count - 2;
        var leftType = request.Stack[left];
        var rightType = request.Stack[left + 1];
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, left, leftType)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, left + 1, rightType)))));
        if (leftType is CliValueKind.ManagedReference or CliValueKind.ManagedAddress ||
            leftType == CliValueKind.NativeInt && rightType == CliValueKind.ManagedAddress)
        {
            if (!stackTypes.Accepts(leftType, rightType) &&
                !stackTypes.Accepts(rightType, leftType))
            {
                throw new InvalidOperationException(
                    "A comparison cannot mix managed address and reference operands.");
            }
            EmitReferenceComparison(code, comparison);
        }
        else
        {
            EmitScalarComparison(code, leftType, comparison);
        }
        StoreResult(request, code, left);
    }

    private void EmitReferenceComparison(
        IWasmInstructionWriter code,
        ComparisonKind comparison)
    {
        if (comparison == ComparisonKind.Equal)
        {
            if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        }
        else if (comparison == ComparisonKind.LessThanUnsigned)
        {
            if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanUnsigned));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        }
        else if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
    }

    private void EmitScalarComparison(
        IWasmInstructionWriter code,
        CliValueKind type,
        ComparisonKind comparison)
    {
        if (comparison == ComparisonKind.Equal)
        {
            EmitEqual(code, type);
        }
        else if (comparison == ComparisonKind.GreaterThanUnsigned)
        {
            EmitGreaterThanUnsignedOrUnordered(code, type);
        }
        else
        {
            EmitLessThanUnsignedOrUnordered(code, type);
        }
    }

    private void EmitEqual(IWasmInstructionWriter code, CliValueKind type)
    {
        switch (type)
        {
            case CliValueKind.I4:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                break;
            case CliValueKind.I8:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
                break;
            case CliValueKind.NativeInt:
                if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
                else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                break;
            case CliValueKind.F4:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Equal));
                break;
            case CliValueKind.F8:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Equal));
                break;
            default:
                throw new InvalidOperationException(
                    "unsupported comparison stack type");
        }
    }

    private void EmitGreaterThanUnsignedOrUnordered(
        IWasmInstructionWriter code,
        CliValueKind type)
    {
        switch (type)
        {
            case CliValueKind.I4:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
                break;
            case CliValueKind.I8:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned));
                break;
            case CliValueKind.NativeInt:
                if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned));
                else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
                break;
            case CliValueKind.F4:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32LessThanOrEqual));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            case CliValueKind.F8:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64LessThanOrEqual));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            default:
                throw new InvalidOperationException(
                    "unsupported unsigned or unordered comparison stack type");
        }
    }

    private void EmitLessThanUnsignedOrUnordered(
        IWasmInstructionWriter code,
        CliValueKind type)
    {
        switch (type)
        {
            case CliValueKind.I4:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
                break;
            case CliValueKind.I8:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanUnsigned));
                break;
            case CliValueKind.NativeInt:
                if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanUnsigned));
                else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
                break;
            case CliValueKind.F4:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32GreaterThanOrEqual));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            case CliValueKind.F8:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64GreaterThanOrEqual));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            default:
                throw new InvalidOperationException(
                    "unsupported unsigned or unordered comparison stack type");
        }
    }

    private void StoreResult(InstructionEmissionRequest request, IWasmInstructionWriter code, int left)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            left,
            CliValueKind.I4)))));
        request.Stack.RemoveAt(left + 1);
        request.Stack[left] = CliValueKind.I4;
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private enum ComparisonKind
    {
        Equal,
        GreaterThanUnsigned,
        LessThanUnsigned,
    }
}
