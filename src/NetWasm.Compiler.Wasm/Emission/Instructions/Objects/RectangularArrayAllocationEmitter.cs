using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class RectangularArrayAllocationEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    ICilTypeOperandResolver types,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.NewRectangularArray,
            InstructionFamily.ArraysFieldsStatics,
            Emit),
    ];

    private void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        roots.Emit(request, code);
        var arrayType = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var elementType = arrayType.ElementType!;
        var firstLengthSlot = request.Stack.Count - arrayType.ArrayRank;
        var scratchOffset = request.Context.ValueLayout.TemporaryOffsets[
            request.Instruction.Offset];
        for (var dimension = 0; dimension < arrayType.ArrayRank; dimension++)
        {
            var lengthLocal = GetStackLocal(
                request.Context,
                firstLengthSlot + dimension,
                CliValueKind.I4);
            Get(code, lengthLocal);
            WriteI32(code, 0);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
            ThrowIf(code, ManagedExceptionKind.Overflow);

            Get(code, request.Context.ValueFrame);
            addresses.Emit(code, checked(scratchOffset + dimension * sizeof(int)));
            addresses.Emit(code, AddressOperation.Add);
            Get(code, lengthLocal);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Store,
                WasmInstructionOperand.Memory(2, 0)));
        }

        WriteI32(code, arrayType.ArrayRank);
        Get(code, request.Context.ValueFrame);
        addresses.Emit(code, scratchOffset);
        addresses.Emit(code, AddressOperation.Add);
        WriteI32(code, typeLayouts.GetObjectLayout(arrayType).TypeId);
        WriteI32(code, typeLayouts.GetObjectLayout(elementType).TypeId);
        var elementsAreReferences = elementType.StackKind == CliValueKind.ManagedReference;
        WriteI32(code, elementsAreReferences ? 0 : values.GetValueLayout(elementType).Size);
        WriteI32(code, elementsAreReferences ? 1 : 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.AllocateRectangularArray))));
        var arrayLocal = GetStackLocal(
            request.Context,
            firstLengthSlot,
            CliValueKind.ManagedReference);
        Set(code, arrayLocal);
        Get(code, arrayLocal);
        addresses.Emit(code, AddressOperation.EqualZero);
        ThrowIf(code, ManagedExceptionKind.OutOfMemory);

        request.Stack.RemoveRange(
            firstLengthSlot + 1,
            arrayType.ArrayRank - 1);
        request.Stack[firstLengthSlot] = CliValueKind.ManagedReference;
    }

    private void ThrowIf(IWasmInstructionWriter code, ManagedExceptionKind kind)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, kind);
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

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
