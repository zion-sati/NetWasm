using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class BoxedValueUnboxEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IValueLayoutProvider values,
    ICilTypeOperandResolver types,
    IImplicitExceptionEmitter exceptions,
    IBoxedValueTypeValidator typeValidator,
    IValueFrameAddressEmitter valueFrameAddresses) : IUnboxEmitter
{
    public void Unbox(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        bool copyValue) =>
        Unbox(request, code, types.Resolve(request.Instruction, request.Header.MethodInstance), copyValue);

    public void Unbox(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        bool copyValue)
    {
        var slot = request.Stack.Count - 1;
        var value = values.GetValueLayout(type);
        var objectLocal = GetStackLocal(
            request.Context,
            slot,
            CliValueKind.ManagedReference);
        EmitNullCheck(code, objectLocal, request.Context.ManagedThrowFunction);
        typeValidator.Validate(code, objectLocal, type, request.Context.ManagedThrowFunction);
        var payloadOffset = WasmTargetLayout.Align(
            layouts.Target.ObjectHeaderSize,
            value.Alignment);
        if (!copyValue)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)objectLocal)));
            addresses.Emit(code, payloadOffset);
            addresses.Emit(code, AddressOperation.Add);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                    request.Context,
                    slot,
                    CliValueKind.ManagedAddress))));
            request.Stack[slot] = CliValueKind.ManagedAddress;
            return;
        }

        if (type.StackKind != CliValueKind.ValueType)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)objectLocal)));
            addresses.Emit(code, payloadOffset);
            addresses.Emit(code, AddressOperation.Add);
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                layouts.Target,
                0,
                type,
                value.Size);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                    request.Context,
                    slot,
                    type.StackKind))));
            request.Stack[slot] = type.StackKind;
            return;
        }

        var temporaryOffset = request.Context.ValueLayout.TemporaryOffsets[
            request.Instruction.Offset];
        valueFrameAddresses.Emit(code, request.Context, temporaryOffset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectLocal)));
        addresses.Emit(code, payloadOffset);
        addresses.Emit(code, AddressOperation.Add);
        SmallBlockEmitter.EmitKnownSizeCopy(code, request.Context, value.Size,
            writer => addresses.Emit(writer, value.Size));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Context,
                slot,
                CliValueKind.ValueType))));
        request.Stack[slot] = CliValueKind.ValueType;
    }

    private void EmitNullCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectLocal)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, throwFunction);
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

}
