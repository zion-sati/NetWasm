// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class IntegerDivRemIntrinsicEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IValueFrameAddressEmitter addresses,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var element = Validate(request);
        var elementLayout = values.GetValueLayout(element);
        var resultLayout = values.GetValueLayout(
            request.Method.Signature.ReturnSignatureType);
        if (resultLayout.Size != elementLayout.Size * 2)
        {
            throw new InvalidOperationException(
                $"{request.Intrinsic} requires a contiguous two-element result.");
        }

        var usesI64 = element.StackKind == CliValueKind.I8 ||
            element.StackKind == CliValueKind.NativeInt && layouts.Target.UsesMemory64;
        var unsigned = IsUnsigned(element);
        var left = request.Local(0, element.StackKind);
        var right = request.Local(1, element.StackKind);
        var savedLeft = usesI64
            ? request.Instruction.Context.NumericTemporaryI8
            : request.Instruction.Context.NumericTemporaryI4;
        if (request.Instruction.Context.TransientAggregatePromotions.ProducerLocals.TryGetValue(
                request.Instruction.Instruction.Offset,
                out var promotedResult))
        {
            EmitPromoted(
                request,
                code,
                element,
                usesI64,
                unsigned,
                left,
                right,
                savedLeft,
                promotedResult);
            return;
        }
        var result = request.Local(0, CliValueKind.ValueType);

        Get(code, left);
        Set(code, savedLeft);
        addresses.Emit(
            code,
            request.Instruction.Context,
            request.Instruction.Context.ValueLayout.TemporaryOffsets[
                request.Instruction.Instruction.Offset]);
        Set(code, result);

        EmitZeroGuard(code, right, usesI64, request.Instruction.Context.ManagedThrowFunction);
        if (!unsigned && RequiresOverflowGuard(element))
        {
            EmitOverflowGuard(code, savedLeft, right, usesI64, request.Instruction.Context.ManagedThrowFunction);
        }

        Get(code, result);
        Get(code, savedLeft);
        Get(code, right);
        code.Write(WasmInstruction.NoOperand(DivideOpcode(usesI64, unsigned)));
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            layouts.Target,
            0,
            element,
            elementLayout.Size);

        Get(code, result);
        Get(code, savedLeft);
        Get(code, result);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            0,
            element,
            elementLayout.Size);
        Get(code, right);
        code.Write(WasmInstruction.NoOperand(
            usesI64 ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        code.Write(WasmInstruction.NoOperand(
            usesI64 ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract));
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            layouts.Target,
            elementLayout.Size,
            element,
            elementLayout.Size);
    }

    private void EmitPromoted(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity element,
        bool usesI64,
        bool unsigned,
        int left,
        int right,
        int savedLeft,
        TransientAggregateScalarLocals result)
    {
        Get(code, left);
        Set(code, savedLeft);

        EmitZeroGuard(code, right, usesI64, request.Instruction.Context.ManagedThrowFunction);
        if (!unsigned && RequiresOverflowGuard(element))
        {
            EmitOverflowGuard(code, savedLeft, right, usesI64, request.Instruction.Context.ManagedThrowFunction);
        }

        Get(code, savedLeft);
        Get(code, right);
        code.Write(WasmInstruction.NoOperand(DivideOpcode(usesI64, unsigned)));
        Set(code, result.First);

        Get(code, savedLeft);
        Get(code, result.First);
        Get(code, right);
        code.Write(WasmInstruction.NoOperand(
            usesI64 ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        code.Write(WasmInstruction.NoOperand(
            usesI64 ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract));
        Set(code, result.Second);
    }

    private static CliTypeIdentity Validate(RuntimeIntrinsicEmissionRequest request)
    {
        if (request.Call.Consumed != 2 ||
            request.Method.Signature.ReturnType != CliValueKind.ValueType ||
            request.Method.Signature.ParameterSignatureTypes is not [var left, var right] ||
            !left.Equals(right) ||
            request.Method.Signature.ReturnSignatureType.TypeArguments is not
                [var quotient, var remainder] ||
            !quotient.Equals(left) ||
            !remainder.Equals(left) ||
            left.CanonicalName is not (
                "primitive:i1" or "primitive:u1" or
                "primitive:i2" or "primitive:u2" or "primitive:char" or
                "primitive:i4" or "primitive:u4" or
                "primitive:i8" or "primitive:u8" or
                "primitive:nativeint" or "primitive:nativeuint"))
        {
            throw new InvalidOperationException(
                $"{request.Intrinsic} requires two identical integral operands " +
                "and a matching ValueTuple result.");
        }

        return left;
    }

    private void EmitZeroGuard(
        IWasmInstructionWriter code,
        int right,
        bool usesI64,
        OptionalFunctionIndex throwFunction)
    {
        Get(code, right);
        code.Write(WasmInstruction.NoOperand(
            usesI64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.DivideByZero, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitOverflowGuard(
        IWasmInstructionWriter code,
        int left,
        int right,
        bool usesI64,
        OptionalFunctionIndex throwFunction)
    {
        Get(code, left);
        code.Write(WasmInstruction.WithOperand(
            usesI64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            usesI64
                ? WasmInstructionOperand.Signed64(long.MinValue)
                : WasmInstructionOperand.Signed(int.MinValue)));
        code.Write(WasmInstruction.NoOperand(
            usesI64 ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal));
        Get(code, right);
        code.Write(WasmInstruction.WithOperand(
            usesI64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            usesI64
                ? WasmInstructionOperand.Signed64(-1)
                : WasmInstructionOperand.Signed(-1)));
        code.Write(WasmInstruction.NoOperand(
            usesI64 ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Overflow, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static byte DivideOpcode(bool usesI64, bool unsigned) =>
        (usesI64, unsigned) switch
        {
            (false, false) => WasmOpcodes.I32DivideSigned,
            (false, true) => WasmOpcodes.I32DivideUnsigned,
            (true, false) => WasmOpcodes.I64DivideSigned,
            (true, true) => WasmOpcodes.I64DivideUnsigned,
        };

    private static bool RequiresOverflowGuard(CliTypeIdentity type) =>
        type.CanonicalName is
            "primitive:i4" or "primitive:i8" or "primitive:nativeint";

    private static bool IsUnsigned(CliTypeIdentity type) =>
        type.CanonicalName is
            "primitive:u1" or "primitive:u2" or
            "primitive:char" or
            "primitive:u4" or "primitive:u8" or
            "primitive:nativeuint";

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
