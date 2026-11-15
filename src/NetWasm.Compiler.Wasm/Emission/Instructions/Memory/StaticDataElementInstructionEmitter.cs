// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal sealed class StaticDataElementInstructionEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IStaticFieldLayoutProvider staticFields,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.LoadStaticDataElement,
            InstructionFamily.ArraysFieldsStatics,
            Emit),
    ];

    private void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var data = request.Instruction.Operand as CilOperand.ReadOnlySpanData ??
            throw Invalid("Static data element metadata is missing.");
        var element = values.GetValueLayout(data.ElementType);
        var staticAddress = staticFields.GetStaticFieldLayout(data.Field).Address;
        Require(request.Stack.Count > 0 && request.Stack[^1] == CliValueKind.I4,
            "Static data element index must be int32.");
        Require(data.ElementCount > 0, "Static data element count is invalid.");
        Require(
            checked(data.ElementCount * element.Size) ==
                data.Field.Definition.InitialData.Length,
            $"Static data size does not match '{data.ElementType}'.");
        Require(
            staticAddress % element.Alignment == 0,
            $"Static data for '{data.ElementType}' is not aligned.");
        Require(
            data.ElementType.StackKind is
                CliValueKind.I4 or
                CliValueKind.I8 or
                CliValueKind.F4 or
                CliValueKind.F8,
            "Static data element type is not scalar.");

        var slot = request.Stack.Count - 1;
        var indexLocal = GetStackLocal(
            request.Context,
            slot,
            CliValueKind.I4);
        Get(code, indexLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(data.ElementCount)));
        code.Write(WasmInstruction.NoOperand(
            WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType((byte)WasmValueTypes.FromCli(
                data.ElementType.StackKind,
                layouts.Target))));
        addresses.Emit(code, staticAddress);
        Get(code, indexLocal);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        addresses.Emit(code, element.Size);
        code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64
            ? WasmOpcodes.I64Multiply
            : WasmOpcodes.I32Multiply));
        addresses.Emit(code, AddressOperation.Add);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            0,
            data.ElementType,
            element.Size);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        exceptions.Emit(code, ManagedExceptionKind.IndexOutOfRange, request.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Context,
                slot,
                data.ElementType.StackKind))));
        request.Stack[slot] = data.ElementType.StackKind;
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static CompilerException Invalid(string message) => new(new(
        DiagnosticCode.RuntimeContract,
        message));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw Invalid(message);
        }
    }
}
