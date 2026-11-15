// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class ArrayLengthAdapter(
    ITargetLayout layouts,
    IImplicitExceptionEmitter exceptions) : IArrayLengthAdapter
{
    public int Adapt(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int stackSlot)
    {
        var sourceType = request.Stack[stackSlot];
        var sourceLocal = GetStackLocal(request.Context, stackSlot, sourceType);
        Get(code, sourceLocal);

        if (sourceType == CliValueKind.NativeInt && layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(int.MaxValue)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned));
            ThrowIf(code, ManagedExceptionKind.Overflow, request.Context.ManagedThrowFunction);

            Get(code, sourceLocal);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
            Set(code, request.Context.NumericTemporaryI4);
            return request.Context.NumericTemporaryI4;
        }

        if (sourceType is not (CliValueKind.I4 or CliValueKind.NativeInt))
        {
            throw new InvalidOperationException(
                "array length must be int32 or native integer");
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        ThrowIf(code, ManagedExceptionKind.Overflow, request.Context.ManagedThrowFunction);
        return sourceLocal;
    }

    private void ThrowIf(
        IWasmInstructionWriter code,
        ManagedExceptionKind kind,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, kind, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
