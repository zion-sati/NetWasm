using System;
using System.Linq;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumCompareToEmitter(
    IEnumStorageResolver storages,
    IEnumNullCheckEmitter nullChecks,
    IEnumValuePairEmitter values,
    IReferenceComparisonEmitter references,
    IImplicitExceptionEmitter exceptions,
    ITargetLayout layouts) : IEnumCompareToEmitter
{
    public void EmitCompareTo(
        IWasmInstructionWriter code,
        CliValueKind leftKind,
        CliTypeIdentity? constrainedType,
        int left,
        int right,
        int result,
        int temporaryReference,
        int temporaryI4)
    {
        if (leftKind == CliValueKind.ManagedAddress)
        {
            EmitConstrained(
                code,
                constrainedType ?? throw new InvalidOperationException(
                    "constrained enum comparison requires its closed receiver type"),
                left,
                right,
                result,
                temporaryReference,
                temporaryI4);
            return;
        }

        nullChecks.Emit(code, left);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(right))));
        references.Emit(code, ReferenceComparison.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(left))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(right))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var storage in storages.Resolve())
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(left))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(storage.Descriptor.TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitComparisonResult(code, storage, left, right, temporaryI4);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        exceptions.Emit(code, ManagedExceptionKind.Argument);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(result))));
    }

    private void EmitConstrained(
        IWasmInstructionWriter code,
        CliTypeIdentity constrainedType,
        int left,
        int right,
        int result,
        int temporaryReference,
        int temporaryI4)
    {
        var storage = storages.Resolve().SingleOrDefault(candidate =>
            candidate.EnumType.Equals(constrainedType));
        if (storage is null)
        {
            throw new InvalidOperationException(
                $"no reachable enum descriptor exists for '{constrainedType.CanonicalName}'");
        }

        WriteLocal(code, WasmOpcodes.LocalGet, left);
        WriteLocal(code, WasmOpcodes.LocalSet, temporaryReference);
        WriteI32(code, 1);
        WriteLocal(code, WasmOpcodes.LocalSet, temporaryI4);
        WriteLocal(code, WasmOpcodes.LocalGet, right);
        references.Emit(code, ReferenceComparison.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteLocal(code, WasmOpcodes.LocalGet, right);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        WriteI32(code, storage.Descriptor.TypeId);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitConstrainedComparisonResult(
            code, storage, temporaryReference, right, temporaryI4);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        exceptions.Emit(code, ManagedExceptionKind.Argument);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        WriteLocal(code, WasmOpcodes.LocalGet, temporaryI4);
        WriteLocal(code, WasmOpcodes.LocalSet, result);
    }

    private void EmitConstrainedComparisonResult(
        IWasmInstructionWriter code,
        EnumStorage storage,
        int left,
        int right,
        int result)
    {
        EmitConstrainedValues(code, storage, left, right);
        EmitLessThan(code, storage);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, -1);
        WriteLocal(code, WasmOpcodes.LocalSet, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitConstrainedValues(code, storage, left, right);
        EmitGreaterThan(code, storage);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, 1);
        WriteLocal(code, WasmOpcodes.LocalSet, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteI32(code, 0);
        WriteLocal(code, WasmOpcodes.LocalSet, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitConstrainedValues(
        IWasmInstructionWriter code,
        EnumStorage storage,
        int left,
        int right)
    {
        WriteLocal(code, WasmOpcodes.LocalGet, left);
        ManagedMemoryEmitter.EmitLoadByType(
            code, layouts.Target, 0, storage.UnderlyingType, storage.Layout.Size);
        WriteLocal(code, WasmOpcodes.LocalGet, right);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            storage.PayloadOffset,
            storage.UnderlyingType,
            storage.Layout.Size);
    }

    private static void EmitLessThan(IWasmInstructionWriter code, EnumStorage storage)
    {
        var signed = IsSigned(storage);
        code.Write(WasmInstruction.NoOperand(storage.Layout.Size == sizeof(long)
            ? signed ? WasmOpcodes.I64LessThanSigned : WasmOpcodes.I64LessThanUnsigned
            : signed ? WasmOpcodes.I32LessThanSigned : WasmOpcodes.I32LessThanUnsigned));
    }

    private static void EmitGreaterThan(IWasmInstructionWriter code, EnumStorage storage)
    {
        var signed = IsSigned(storage);
        code.Write(WasmInstruction.NoOperand(storage.Layout.Size == sizeof(long)
            ? signed ? WasmOpcodes.I64GreaterThanSigned : WasmOpcodes.I64GreaterThanUnsigned
            : signed ? WasmOpcodes.I32GreaterThanSigned : WasmOpcodes.I32GreaterThanUnsigned));
    }

    private static bool IsSigned(EnumStorage storage) =>
        storage.UnderlyingType.CanonicalName is
            "primitive:i1" or "primitive:i2" or "primitive:i4" or "primitive:i8";

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteLocal(
        IWasmInstructionWriter code,
        byte opcode,
        int local) => code.Write(WasmInstruction.WithOperand(
        opcode,
        WasmInstructionOperand.Unsigned((uint)local)));

    private void EmitComparisonResult(
        IWasmInstructionWriter code,
        EnumStorage storage,
        int left,
        int right,
        int result)
    {
        var signed = IsSigned(storage);
        values.Emit(code, storage, left, right);
        if (storage.Layout.Size == sizeof(long))
        {
            if (signed) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanSigned));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanUnsigned));
        }
        else if (signed) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(-1)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(result))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        values.Emit(code, storage, left, right);
        if (storage.Layout.Size == sizeof(long))
        {
            if (signed) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanSigned));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned));
        }
        else if (signed) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanSigned));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(result))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(result))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
