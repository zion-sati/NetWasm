using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class LocalsArgumentsEmitter(
    ISymbolFormatter symbols,
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IArgumentTypeResolver argumentTypes,
    IArgumentSignatureTypeResolver argumentSignatureTypes,
    IValueFrameAddressEmitter valueFrameAddresses,
    IFilterEnvironmentRootEmitter filterEnvironmentRoots) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.LoadArgument, EmitLoadArgument),
        Command(CilOperation.LoadArgumentAddress, EmitLoadArgumentAddress),
        Command(CilOperation.StoreArgument, EmitStoreArgument),
        Command(CilOperation.LoadLocal, EmitLoadLocal),
        Command(CilOperation.LoadLocalAddress, EmitLoadLocalAddress),
        Command(CilOperation.StoreLocal, EmitStoreLocal),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.ConstantsStackLocalsArguments,
        emit);

    private void EmitLoadArgument(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var index = CilOperandReader.GetIndex(request.Instruction);
        if (TryGetFilterCapture(request.Context, true, index, out var capture))
        {
            EmitLoadFilterCapture(request, code, capture);
            return;
        }

        var type = argumentSignatureTypes.Resolve(request.Header, index);
        if (argumentTypes.Resolve(request.Header, index) == CliValueKind.ValueType)
        {
            var temporaryOffset =
                request.Context.ValueLayout.TemporaryOffsets[request.Instruction.Offset];
            valueFrameAddresses.Emit(code, request.Context, temporaryOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(index + request.Context.ParameterOffset))));
            EmitAddressConstant(code, values.GetValueLayout(type).Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            PushLocal(request, code, CliValueKind.ValueType);
            return;
        }
        if (request.Context.ValueLayout.ArgumentOffsets.TryGetValue(index, out var offset))
        {
            valueFrameAddresses.Emit(code, request.Context, offset);
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                layouts.Target,
                0,
                type,
                layouts.Target.GetStorageSize(type));
            PushLocal(request, code, type.StackKind);
            return;
        }
        PushFromLocal(
            request, code, index + request.Context.ParameterOffset,
            argumentTypes.Resolve(request.Header, index));
    }

    private void EmitLoadLocal(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var index = CilOperandReader.GetIndex(request.Instruction);
        if (TryGetFilterCapture(request.Context, false, index, out var capture))
        {
            EmitLoadFilterCapture(request, code, capture);
            return;
        }

        var localType = request.Header.LocalSignatureTypes[index];
        if (localType.StackKind == CliValueKind.ValueType)
        {
            var offset = request.Context.ValueLayout.TemporaryOffsets[
                request.Instruction.Offset];
            valueFrameAddresses.Emit(code, request.Context, offset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.LocalBase + index))));
            EmitAddressConstant(code, values.GetValueLayout(localType).Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            PushLocal(request, code, CliValueKind.ValueType);
            return;
        }
        if (request.Context.ValueLayout.SpilledScalarLocals.Contains(index))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.LocalBase + index))));
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                layouts.Target,
                0,
                localType,
                layouts.Target.GetStorageSize(localType));
            PushLocal(request, code, localType.StackKind);
            return;
        }
        PushFromLocal(
            request, code, request.Context.LocalBase + index,
            request.Header.Locals[index]);
    }

    private void EmitStoreLocal(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var localIndex = CilOperandReader.GetIndex(request.Instruction);
        var slot = request.Stack.Count - 1;
        var localType = request.Header.LocalSignatureTypes[localIndex];
        if (TryGetFilterCapture(request.Context, false, localIndex, out var capture))
        {
            EmitStoreFilterCapture(request, code, capture);
            return;
        }
        if (localType.StackKind == CliValueKind.ValueType)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.LocalBase + localIndex))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                CliValueKind.ValueType)))));
            EmitAddressConstant(code, values.GetValueLayout(localType).Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
            request.Stack.RemoveAt(slot);
            return;
        }
        if (request.Context.ValueLayout.SpilledScalarLocals.Contains(localIndex))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.LocalBase + localIndex))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, request.Stack[slot])))));
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                localType,
                layouts.Target.GetStorageSize(localType));
            request.Stack.RemoveAt(slot);
            return;
        }
        PopToLocal(request, code, request.Context.LocalBase + localIndex);
    }

    private void EmitStoreArgument(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var argumentIndex = CilOperandReader.GetIndex(request.Instruction);
        var targetLocal = argumentIndex + request.Context.ParameterOffset;
        var type = argumentSignatureTypes.Resolve(request.Header, argumentIndex);
        if (TryGetFilterCapture(request.Context, true, argumentIndex, out var capture))
        {
            EmitStoreFilterCapture(request, code, capture);
            return;
        }
        if (type.StackKind == CliValueKind.ValueType)
        {
            var slot = request.Stack.Count - 1;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(targetLocal))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                CliValueKind.ValueType)))));
            EmitAddressConstant(code, values.GetValueLayout(type).Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
            request.Stack.RemoveAt(slot);
            return;
        }
        if (request.Context.ValueLayout.ArgumentOffsets.TryGetValue(
                argumentIndex,
                out var argumentOffset))
        {
            var slot = request.Stack.Count - 1;
            valueFrameAddresses.Emit(code, request.Context, argumentOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, request.Stack[slot])))));
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                type,
                layouts.Target.GetStorageSize(type));
            request.Stack.RemoveAt(slot);
            return;
        }
        PopToLocal(request, code, targetLocal);
    }

    private void EmitLoadLocalAddress(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var localIndex = CilOperandReader.GetIndex(request.Instruction);
        if (TryGetFilterCapture(request.Context, false, localIndex, out var capture))
        {
            EmitLoadFilterCaptureAddress(request, code, capture);
            return;
        }
        var localType = request.Header.LocalSignatureTypes[localIndex];
        if (localType.StackKind != CliValueKind.ValueType &&
            !request.Context.ValueLayout.SpilledScalarLocals.Contains(localIndex))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedCil,
                $"{symbols.Format(request.Header.Method)} at " +
                $"IL_{request.Instruction.Offset:x4}: address-taking scalar local " +
                $"'{localType.CanonicalName}' is not yet supported"));
        }
        PushFromLocal(
            request, code, request.Context.LocalBase + localIndex,
            CliValueKind.ManagedAddress);
    }

    private void EmitLoadArgumentAddress(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var argumentIndex = CilOperandReader.GetIndex(request.Instruction);
        if (TryGetFilterCapture(request.Context, true, argumentIndex, out var capture))
        {
            EmitLoadFilterCaptureAddress(request, code, capture);
            return;
        }
        if (request.Context.ValueLayout.ArgumentOffsets.TryGetValue(
                argumentIndex,
                out var offset))
        {
            valueFrameAddresses.Emit(code, request.Context, offset);
            PushLocal(request, code, CliValueKind.ManagedAddress);
            return;
        }
        if (argumentTypes.Resolve(request.Header, argumentIndex) is not (
                CliValueKind.ManagedAddress or CliValueKind.ValueType))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedCil,
                $"{symbols.Format(request.Header.Method)} at " +
                $"IL_{request.Instruction.Offset:x4}: address-taking a scalar argument " +
                "requires an address spill"));
        }
        var parameterOffset = (request.Header.MethodInstance?.Signature ??
                request.Header.Method.Signature).ReturnSignatureType.StackKind ==
            CliValueKind.ValueType ? 1 : 0;
        PushFromLocal(
            request, code, argumentIndex + parameterOffset,
            CliValueKind.ManagedAddress);
    }

    private void EmitLoadFilterCapture(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        FilterCapture capture)
    {
        valueFrameAddresses.Emit(code, request.Context, capture);
        if (capture.Type.StackKind != CliValueKind.ValueType)
        {
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                layouts.Target,
                0,
                capture.Type,
                layouts.Target.GetStorageSize(capture.Type));
        }
        PushLocal(
            request, code, capture.Type.StackKind == CliValueKind.ValueType
                ? CliValueKind.ValueType
                : capture.Type.StackKind);
    }

    private void EmitLoadFilterCaptureAddress(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        FilterCapture capture)
    {
        valueFrameAddresses.Emit(code, request.Context, capture);
        PushLocal(request, code, CliValueKind.ManagedAddress);
    }

    private void EmitStoreFilterCapture(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        FilterCapture capture)
    {
        var slot = request.Stack.Count - 1;
        valueFrameAddresses.Emit(code, request.Context, capture);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            slot,
            request.Stack[slot])))));
        if (capture.Type.StackKind == CliValueKind.ValueType)
        {
            EmitAddressConstant(code, values.GetValueLayout(capture.Type).Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        }
        else
        {
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                capture.Type,
                layouts.Target.GetStorageSize(capture.Type));
        }
        request.Stack.RemoveAt(slot);
        filterEnvironmentRoots.Emit(code, request.Context);
    }

    private static bool TryGetFilterCapture(
        MethodEmissionContext context,
        bool isArgument,
        int index,
        out FilterCapture capture)
    {
        if (context.FilterEnvironment.Captures.TryGetValue(
                new CapturedSlot(isArgument, index),
                out var found))
        {
            capture = found;
            return true;
        }
        capture = null!;
        return false;
    }

    private void PushFromLocal(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int sourceLocal,
        CliValueKind type)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(sourceLocal))));
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

    private void PopToLocal(InstructionEmissionRequest request, IWasmInstructionWriter code, int targetLocal)
    {
        var slot = request.Stack.Count - 1;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            slot,
            request.Stack[slot])))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(targetLocal))));
        request.Stack.RemoveAt(slot);
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(value)));
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
