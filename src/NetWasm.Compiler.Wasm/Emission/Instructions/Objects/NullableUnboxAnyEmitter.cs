using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class NullableUnboxAnyEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IImplicitExceptionEmitter exceptions) : INullableUnboxAnyEmitter
{
    public void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity nullableType,
        CliTypeIdentity underlyingType)
    {
        var slot = request.Stack.Count - 1;
        var sourceLocal = GetStackLocal(request, slot, CliValueKind.ManagedReference);
        var targetLocal = GetStackLocal(request, slot, CliValueKind.ValueType);
        var nullable = values.GetValueLayout(nullableType);
        var underlying = values.GetValueLayout(underlyingType);
        var boxed = typeLayouts.GetObjectLayout(underlyingType);
        var targetOffset = request.Context.ValueLayout.TemporaryOffsets[
            request.Instruction.Offset];

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ValueFrame)));
        addresses.Emit(code, targetOffset);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        addresses.Emit(code, nullable.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceLocal)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitTypeCheck(code, sourceLocal, boxed.TypeId);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store8,
            WasmInstructionOperand.Memory(0, 0)));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        addresses.Emit(code, Align(sizeof(byte), underlying.Alignment));
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceLocal)));
        addresses.Emit(code, WasmTargetLayout.Align(
            layouts.Target.ObjectHeaderSize,
            underlying.Alignment));
        addresses.Emit(code, AddressOperation.Add);
        addresses.Emit(code, underlying.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)targetLocal)));
        request.Stack[slot] = CliValueKind.ValueType;
    }

    private void EmitTypeCheck(
        IWasmInstructionWriter code,
        int sourceLocal,
        int expectedTypeId)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(expectedTypeId)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.InvalidCast);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private int GetStackLocal(
        InstructionEmissionRequest request,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        request.Context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);
}
