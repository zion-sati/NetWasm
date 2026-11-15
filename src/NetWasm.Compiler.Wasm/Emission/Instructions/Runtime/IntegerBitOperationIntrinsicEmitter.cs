// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class IntegerBitOperationIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var valueType = request.Instruction.Stack[request.ArgumentBase];
        var usesI64 = valueType == CliValueKind.I8 ||
            valueType == CliValueKind.NativeInt && request.Target.UsesMemory64;
        if (valueType is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt))
        {
            throw new InvalidOperationException(
                $"{request.Intrinsic} requires an integer argument.");
        }

        if (request.Intrinsic is RuntimeIntrinsic.IntegerRotateLeft or
            RuntimeIntrinsic.IntegerRotateRight)
        {
            EmitRotate(request, code, valueType, usesI64);
            return;
        }

        var opcode = request.Intrinsic switch
        {
            RuntimeIntrinsic.IntegerLeadingZeroCount or RuntimeIntrinsic.IntegerLog2 =>
                usesI64
                    ? WasmOpcodes.I64CountLeadingZeros
                    : WasmOpcodes.I32CountLeadingZeros,
            RuntimeIntrinsic.IntegerPopulationCount => usesI64
                ? WasmOpcodes.I64PopulationCount
                : WasmOpcodes.I32PopulationCount,
            RuntimeIntrinsic.IntegerTrailingZeroCount => usesI64
                ? WasmOpcodes.I64CountTrailingZeros
                : WasmOpcodes.I32CountTrailingZeros,
            _ => throw new InvalidOperationException(
                $"Unsupported integer bit intrinsic '{request.Intrinsic}'."),
        };

        Get(code, request.Local(0, valueType));
        if (request.Intrinsic == RuntimeIntrinsic.IntegerLog2)
        {
            Constant(code, usesI64, 1);
            code.Write(WasmInstruction.NoOperand(
                usesI64 ? WasmOpcodes.I64Or : WasmOpcodes.I32Or));
        }
        code.Write(WasmInstruction.NoOperand(opcode));

        if (request.Intrinsic == RuntimeIntrinsic.IntegerLog2)
        {
            Constant(code, usesI64, usesI64 ? 63 : 31);
            code.Write(WasmInstruction.NoOperand(
                usesI64 ? WasmOpcodes.I64Xor : WasmOpcodes.I32Xor));
        }
        if (usesI64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        }
        Set(code, request.Local(0, CliValueKind.I4));
    }

    private static void EmitRotate(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliValueKind valueType,
        bool usesI64)
    {
        if (request.Call.Consumed != 2 ||
            request.Instruction.Stack[request.ArgumentBase + 1] != CliValueKind.I4)
        {
            throw new InvalidOperationException(
                $"{request.Intrinsic} requires an integer value and Int32 offset.");
        }

        Get(code, request.Local(0, valueType));
        Get(code, request.Local(1, CliValueKind.I4));
        if (usesI64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        var rotateLeft = request.Intrinsic == RuntimeIntrinsic.IntegerRotateLeft;
        code.Write(WasmInstruction.NoOperand(usesI64
            ? rotateLeft ? WasmOpcodes.I64RotateLeft : WasmOpcodes.I64RotateRight
            : rotateLeft ? WasmOpcodes.I32RotateLeft : WasmOpcodes.I32RotateRight));
        Set(code, request.Local(0, valueType));
    }

    private static void Constant(
        IWasmInstructionWriter code,
        bool usesI64,
        int value) => code.Write(WasmInstruction.WithOperand(
            usesI64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            usesI64
                ? WasmInstructionOperand.Signed64(value)
                : WasmInstructionOperand.Signed(value)));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
