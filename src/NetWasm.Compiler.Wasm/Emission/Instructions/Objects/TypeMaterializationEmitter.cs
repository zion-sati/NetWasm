using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class TypeMaterializationEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.MaterializeType,
            InstructionFamily.AllocationBoxingTypes,
            EmitTypeFromHandle),
        new(
            CilOperation.GetObjectType,
            InstructionFamily.AllocationBoxingTypes,
            EmitObjectType),
    ];

    private void EmitTypeFromHandle(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitCommand(request, code, false);

    private void EmitObjectType(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmitCommand(
            request,
            code,
            true,
            request.Instruction.Operand is CilOperand.TypeIdentity constrained
                ? constrained.Value
                : null);
    }

    private void EmitCommand(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        bool readObjectHeader,
        CliTypeIdentity? constrainedType = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        roots.Emit(request, code);
        Materialize(
            code,
            request.Stack,
            request.Context,
            readObjectHeader,
            constrainedType);
    }

    private void Materialize(
        IWasmInstructionWriter code,
        List<CliValueKind> stack,
        MethodEmissionContext context,
        bool readObjectHeader,
        CliTypeIdentity? constrainedType)
    {
        var slot = stack.Count - 1;
        var typeIdLocal = GetStackLocal(context, slot, CliValueKind.I4);
        var typeLocal = GetStackLocal(context, slot, CliValueKind.ManagedReference);
        if (constrainedType is { IsValueType: true })
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(
                    typeLayouts.GetObjectLayout(constrainedType).TypeId)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
        }
        else if (readObjectHeader)
        {
            if (constrainedType is not null)
            {
                var addressLocal = GetStackLocal(
                    context,
                    slot,
                    CliValueKind.ManagedAddress);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)addressLocal)));
                ManagedMemoryEmitter.EmitReferenceLoad(code, layouts.Target, 0);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)typeLocal)));
            }
            EmitNullCheck(code, typeLocal);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(typeLocal))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(typeIdLocal))));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(typeIdLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(typeIdLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.TypeTypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.GetTypeObject)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(typeLocal))));
        EmitAllocationFailureCheck(code, typeLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        stack[slot] = CliValueKind.ManagedReference;
    }

    private void EmitNullCheck(IWasmInstructionWriter code, int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitAllocationFailureCheck(IWasmInstructionWriter code, int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitReferenceEqualZero(IWasmInstructionWriter code)
    {
        addresses.Emit(code, AddressOperation.EqualZero);
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
