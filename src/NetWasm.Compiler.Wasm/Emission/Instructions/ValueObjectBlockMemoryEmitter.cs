using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class ValueObjectBlockMemoryEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    ICilTypeOperandResolver types,
    IRuntimeImportResolver runtimeImports) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.InitializeObject, EmitInitializeObject),
        Command(CilOperation.SizeOf, EmitSizeOf),
        Command(CilOperation.LocalAllocate, EmitLocalAllocate),
        Command(CilOperation.CopyBlock, EmitCopyBlock),
        Command(CilOperation.InitializeBlock, EmitInitializeBlock),
        Command(CilOperation.DefaultValue, EmitDefaultValue),
        Command(CilOperation.LoadObject, EmitLoadObject),
        Command(CilOperation.StoreObject, EmitStoreObject),
        Command(CilOperation.CopyObject, EmitCopyObject),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.ValueObjectBlockMemory,
        emit);

    private void EmitSizeOf(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitConstant(
        request, code, values.GetValueLayout(types.Resolve(request.Instruction, request.Header.MethodInstance)).Size,
        CliValueKind.I4);

    private void EmitInitializeObject(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var slot = request.Stack.Count - 1;
        var layout = values.GetValueLayout(types.Resolve(request.Instruction, request.Header.MethodInstance));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, request.Stack[slot])))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        EmitAddressConstant(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        request.Stack.RemoveAt(slot);
    }

    private void EmitLocalAllocate(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var slot = request.Stack.Count - 1;
        var sizeType = request.Stack[slot];
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, sizeType)))));
        if (layouts.Target.UsesMemory64 && sizeType == CliValueKind.I4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            slot,
            CliValueKind.NativeInt)))));
        request.Stack[slot] = CliValueKind.NativeInt;
    }

    private void EmitCopyBlock(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var destination = request.Stack.Count - 3;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination,
            request.Stack[destination])))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination + 1,
            request.Stack[destination + 1])))));
        EmitBlockLength(request, code, destination + 2);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        request.Stack.RemoveRange(destination, 3);
    }

    private void EmitInitializeBlock(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var destination = request.Stack.Count - 3;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination,
            request.Stack[destination])))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination + 1,
            CliValueKind.I4)))));
        EmitBlockLength(request, code, destination + 2);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        request.Stack.RemoveRange(destination, 3);
    }

    private void EmitBlockLength(InstructionEmissionRequest request, IWasmInstructionWriter code, int slot)
    {
        var type = request.Stack[slot];
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, type)))));
        if (layouts.Target.UsesMemory64 && type == CliValueKind.I4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
    }

    private void EmitDefaultValue(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        if (type.StackKind != CliValueKind.ValueType)
        {
            EmitConstant(request, code, 0, type.StackKind);
            return;
        }
        var layout = values.GetValueLayout(type);
        var offset = request.Context.ValueLayout.TemporaryOffsets[
            request.Instruction.Offset];
        EmitValueFrameAddress(code, request.Context, offset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        EmitAddressConstant(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        PushLocal(request, code, CliValueKind.ValueType);
    }

    private void EmitLoadObject(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var slot = request.Stack.Count - 1;
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var layout = values.GetValueLayout(type);
        if (type.StackKind == CliValueKind.ValueType)
        {
            var offset = request.Context.ValueLayout.TemporaryOffsets[
                request.Instruction.Offset];
            EmitValueFrameAddress(code, request.Context, offset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                request.Stack[slot])))));
            EmitAddressConstant(code, layout.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                CliValueKind.ValueType)))));
            request.Stack[slot] = CliValueKind.ValueType;
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            slot,
            request.Stack[slot])))));
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            0,
            type,
            layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, type.StackKind)))));
        request.Stack[slot] = type.StackKind;
    }

    private void EmitStoreObject(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var destination = request.Stack.Count - 2;
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var layout = values.GetValueLayout(type);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination,
            request.Stack[destination])))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination + 1,
            type.StackKind)))));
        if (type.StackKind == CliValueKind.ValueType)
        {
            EmitAddressConstant(code, layout.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        }
        else
        {
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                type,
                layout.Size);
        }
        request.Stack.RemoveRange(destination, 2);
    }

    private void EmitCopyObject(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var destination = request.Stack.Count - 2;
        var layout = values.GetValueLayout(types.Resolve(request.Instruction, request.Header.MethodInstance));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination,
            request.Stack[destination])))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination + 1,
            request.Stack[destination + 1])))));
        EmitAddressConstant(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        request.Stack.RemoveRange(destination, 2);
    }

    private void EmitConstant(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int value,
        CliValueKind type)
    {
        if (layouts.Target.UsesMemory64 &&
            type is CliValueKind.ManagedReference or CliValueKind.ManagedAddress)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        }
        PushLocal(request, code, type);
    }

    private void PushLocal(InstructionEmissionRequest request, IWasmInstructionWriter code, CliValueKind type)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            request.Stack.Count,
            type)))));
        request.Stack.Add(type);
    }

    private void EmitValueFrameAddress(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        int offset)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ValueFrame))));
        if (offset == 0)
        {
            return;
        }
        EmitAddressConstant(code, offset);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        }
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
