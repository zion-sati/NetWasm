using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class StringConstructionEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IStringConstructionPlanResolver plans) : IStringConstructionEmitter
{
    public void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase)
    {
        var plan = plans.Resolve(signature, request.Instruction.Offset);
        switch (plan.SourceKind)
        {
            case StringConstructionSourceKind.RepeatedCharacter:
                EmitRepeatedCharacter(request, code, signature, argumentBase);
                return;
            case StringConstructionSourceKind.CharacterArray:
                EmitCharacterArray(
                    request,
                    code,
                    signature,
                    argumentBase,
                    plan);
                return;
            default:
                throw new InvalidOperationException(
                    $"Unknown string construction source kind '{plan.SourceKind}'.");
        }
    }

    private void EmitRepeatedCharacter(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase)
    {
        var lengthLocal = GetStackLocal(
            request.Context,
            argumentBase + 1,
            CliValueKind.I4);
        EmitArgumentOutOfRangeIfNegative(code, lengthLocal);
        code.Write(LocalGet(lengthLocal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(
                (int)((uint.MaxValue - 2 * sizeof(int)) / sizeof(char)))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(LocalGet(GetStackLocal(
            request.Context,
            argumentBase,
            request.Stack[argumentBase])));
        code.Write(LocalGet(lengthLocal));
        Allocate(request, code);
        Publish(request, code, signature, argumentBase);
    }

    private void EmitCharacterArray(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase,
        StringConstructionPlan plan)
    {
        var arrayLocal = GetStackLocal(
            request.Context,
            argumentBase,
            CliValueKind.ManagedReference);
        code.Write(LocalGet(arrayLocal));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.ArgumentNull);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        var startLocal = plan.StartArgumentIndex is { } startArgumentIndex
            ? GetStackLocal(
                request.Context,
                argumentBase + startArgumentIndex,
                CliValueKind.I4)
            : (int?)null;
        var lengthLocal = plan.LengthArgumentIndex is { } lengthArgumentIndex
            ? GetStackLocal(
                request.Context,
                argumentBase + lengthArgumentIndex,
                CliValueKind.I4)
            : (int?)null;
        if (startLocal is { } start)
        {
            EmitArgumentOutOfRangeIfNegative(code, start);
        }
        if (lengthLocal is { } length)
        {
            EmitArgumentOutOfRangeIfNegative(code, length);
        }
        if (startLocal is { } boundedStart)
        {
            var boundedLength = lengthLocal!.Value;
            code.Write(LocalGet(boundedStart));
            EmitArrayLength(code, arrayLocal);
            code.Write(LocalGet(boundedLength));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanSigned));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.ArgumentOutOfRange);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        EmitCharacterCount(code, arrayLocal, lengthLocal);
        Allocate(request, code);

        code.Write(LocalGet(request.Context.ObjectTemporary));
        addresses.Emit(code, objects.StringDataOffset);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(LocalGet(arrayLocal));
        ManagedMemoryEmitter.EmitLoadBySize(
            code,
            layouts.Target,
            objects.ArrayDataPointerOffset,
            layouts.Target.AddressSize);
        if (startLocal is { } copyStart)
        {
            code.Write(LocalGet(copyStart));
            if (layouts.Target.UsesMemory64)
            {
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            }
            addresses.Emit(code, sizeof(char));
            code.Write(WasmInstruction.NoOperand(
                layouts.Target.UsesMemory64
                    ? WasmOpcodes.I64Multiply
                    : WasmOpcodes.I32Multiply));
            addresses.Emit(code, AddressOperation.Add);
        }
        EmitCharacterCount(code, arrayLocal, lengthLocal);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        addresses.Emit(code, sizeof(char));
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64 ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        Publish(request, code, signature, argumentBase);
    }

    private void EmitArgumentOutOfRangeIfNegative(
        IWasmInstructionWriter code,
        int local)
    {
        code.Write(LocalGet(local));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.ArgumentOutOfRange);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitCharacterCount(
        IWasmInstructionWriter code,
        int arrayLocal,
        int? lengthLocal)
    {
        if (lengthLocal is { } length)
        {
            code.Write(LocalGet(length));
            return;
        }
        EmitArrayLength(code, arrayLocal);
    }

    private void Allocate(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(typeLayouts.StringTypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)runtimeImports.Resolve(RuntimeImportSymbol.AllocateString))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(LocalGet(request.Context.ObjectTemporary));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void Publish(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase)
    {
        code.Write(LocalGet(request.Context.ObjectTemporary));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Context,
                argumentBase,
                CliValueKind.ManagedReference))));
        request.Stack.RemoveRange(argumentBase, signature.ParameterTypes.Length);
        request.Stack.Add(CliValueKind.ManagedReference);
    }

    private void EmitArrayLength(IWasmInstructionWriter code, int arrayLocal)
    {
        code.Write(LocalGet(arrayLocal));
        ManagedMemoryEmitter.EmitLoadBySize(
            code,
            layouts.Target,
            objects.ArrayLengthOffset,
            sizeof(int));
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static WasmInstruction LocalGet(int local) =>
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local));
}
