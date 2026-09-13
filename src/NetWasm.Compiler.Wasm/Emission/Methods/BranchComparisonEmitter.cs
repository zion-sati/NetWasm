using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class BranchComparisonEmitter(
    ITargetLayout layouts,
    IStackTypeCompatibilityValidator stackTypes) :
    IBranchComparisonEmitter
{
    public void Compare(
        IWasmInstructionWriter code,
        CilOperation operation,
        CliValueKind leftType,
        CliValueKind rightType)
    {
        if (leftType is CliValueKind.ManagedReference or CliValueKind.ManagedAddress ||
            leftType == CliValueKind.NativeInt && rightType == CliValueKind.ManagedAddress)
        {
            if (!stackTypes.Accepts(leftType, rightType) &&
                !stackTypes.Accepts(rightType, leftType))
            {
                throw new InvalidOperationException(
                    "A branch comparison cannot mix managed address and reference operands.");
            }
            switch (operation)
            {
                case CilOperation.BranchIfEqual:
                    EmitReferenceEqual(code);
                    return;
                case CilOperation.BranchIfNotEqual:
                    EmitReferenceEqual(code);
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                    return;
                case CilOperation.BranchIfGreaterThanUnsigned:
                    if (layouts.Target.UsesMemory64)
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned));
                    }
                    else
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
                    }
                    return;
                case CilOperation.BranchIfLessThanUnsigned:
                    if (layouts.Target.UsesMemory64)
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanUnsigned));
                    }
                    else
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
                    }
                    return;
                case CilOperation.BranchIfGreaterThanOrEqualUnsigned:
                    if (layouts.Target.UsesMemory64)
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanUnsigned));
                    }
                    else
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
                    }
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                    return;
                case CilOperation.BranchIfLessThanOrEqualUnsigned:
                    if (layouts.Target.UsesMemory64)
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned));
                    }
                    else
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
                    }
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                    return;
            }
        }
        if (leftType != rightType)
        {
            throw new InvalidOperationException(
                $"A branch comparison has incompatible scalar operands: " +
                $"left {leftType}, right {rightType}.");
        }
        if (leftType == CliValueKind.NativeInt)
        {
            if (layouts.Target.UsesMemory64)
            {
                EmitTypedBranchComparison(code, operation, CliValueKind.I8);
                return;
            }
        }
        if (leftType is CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8)
        {
            EmitTypedBranchComparison(code, operation, leftType);
            return;
        }
        switch (operation)
        {
            case CilOperation.BranchIfEqual:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                break;
            case CilOperation.BranchIfNotEqual:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            case CilOperation.BranchIfGreaterThanSigned:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanSigned));
                break;
            case CilOperation.BranchIfGreaterThanUnsigned:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
                break;
            case CilOperation.BranchIfGreaterThanOrEqualSigned:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            case CilOperation.BranchIfGreaterThanOrEqualUnsigned:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            case CilOperation.BranchIfLessThanSigned:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
                break;
            case CilOperation.BranchIfLessThanUnsigned:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
                break;
            case CilOperation.BranchIfLessThanOrEqualSigned:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanSigned));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            case CilOperation.BranchIfLessThanOrEqualUnsigned:
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static void EmitTypedBranchComparison(
        IWasmInstructionWriter code,
        CilOperation operation,
        CliValueKind type)
    {
        switch (type)
        {
            case CliValueKind.I8:
                switch (operation)
                {
                    case CilOperation.BranchIfEqual: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal)); return;
                    case CilOperation.BranchIfNotEqual: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfGreaterThanSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanSigned)); return;
                    case CilOperation.BranchIfGreaterThanUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned)); return;
                    case CilOperation.BranchIfGreaterThanOrEqualSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanSigned)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfGreaterThanOrEqualUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanUnsigned)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfLessThanSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanSigned)); return;
                    case CilOperation.BranchIfLessThanUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanUnsigned)); return;
                    case CilOperation.BranchIfLessThanOrEqualSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanSigned)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfLessThanOrEqualUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                }
                break;
            case CliValueKind.F4:
                switch (operation)
                {
                    case CilOperation.BranchIfEqual: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Equal)); return;
                    case CilOperation.BranchIfNotEqual: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Equal)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfGreaterThanSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32GreaterThan)); return;
                    case CilOperation.BranchIfLessThanSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32LessThan)); return;
                    case CilOperation.BranchIfGreaterThanOrEqualSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32GreaterThanOrEqual)); return;
                    case CilOperation.BranchIfLessThanOrEqualSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32LessThanOrEqual)); return;
                    case CilOperation.BranchIfGreaterThanUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32LessThanOrEqual)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfGreaterThanOrEqualUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32LessThan)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfLessThanUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32GreaterThanOrEqual)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfLessThanOrEqualUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32GreaterThan)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                }
                break;
            case CliValueKind.F8:
                switch (operation)
                {
                    case CilOperation.BranchIfEqual: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Equal)); return;
                    case CilOperation.BranchIfNotEqual: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Equal)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfGreaterThanSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64GreaterThan)); return;
                    case CilOperation.BranchIfLessThanSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64LessThan)); return;
                    case CilOperation.BranchIfGreaterThanOrEqualSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64GreaterThanOrEqual)); return;
                    case CilOperation.BranchIfLessThanOrEqualSigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64LessThanOrEqual)); return;
                    case CilOperation.BranchIfGreaterThanUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64LessThanOrEqual)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfGreaterThanOrEqualUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64LessThan)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfLessThanUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64GreaterThanOrEqual)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                    case CilOperation.BranchIfLessThanOrEqualUnsigned: code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64GreaterThan)); code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)); return;
                }
                break;
        }
        throw new InvalidOperationException($"unsupported branch comparison '{operation}' for '{type}'");
    }

    private void EmitReferenceEqual(IWasmInstructionWriter code)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
        }
        else
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        }
    }
}
