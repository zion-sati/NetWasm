using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class MemberMaterializationEmitter(
    ITargetLayout layouts,
    IMemberDescriptorLayout descriptors,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.MaterializeMethod),
        Command(CilOperation.MaterializeField),
    ];

    private InstructionCommand Command(CilOperation operation) => new(
        operation,
        InstructionFamily.AllocationBoxingTypes,
        Emit);

    private void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Instruction.Operand is not CilOperand.Index count)
        {
            throw new InvalidOperationException(
                "Member materialization requires an arity operand.");
        }
        var arity = count.Value;
        if (arity is not (1 or 2))
        {
            throw new InvalidOperationException("Member materialization arity is invalid.");
        }

        var slot = request.Stack.Count - arity;
        var source = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            request.Context.StackLocals,
            slot,
            CliValueKind.NativeInt,
            layouts.Target);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)source)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Argument);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        if (arity == 1)
        {
            EmitOneArgumentValidation(code, source);
        }
        else
        {
            var declaringType = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                request.Context.StackLocals,
                slot + 1,
                CliValueKind.I4,
                layouts.Target);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)declaringType)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.Argument);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            EmitTwoArgumentValidation(code, source, declaringType);
            request.Stack.RemoveAt(slot + 1);
        }

        var destination = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            request.Context.StackLocals,
            slot,
            CliValueKind.ManagedReference,
            layouts.Target);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)source)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)destination)));
        request.Stack[slot] = CliValueKind.ManagedReference;
    }

    private void EmitOneArgumentValidation(
        IWasmInstructionWriter code,
        int source)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)source)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(
                2,
                (uint)descriptors.RequiresDeclaringTypeOffset)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Argument);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitTwoArgumentValidation(
        IWasmInstructionWriter code,
        int source,
        int declaringType)
    {
        EmitDescriptorDeclaringTypeId(code, source);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)declaringType)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));

        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Argument);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitDescriptorDeclaringTypeId(
        IWasmInstructionWriter code,
        int source)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)source)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(
                2,
                (uint)descriptors.DeclaringTypeIdOffset)));
    }
}
