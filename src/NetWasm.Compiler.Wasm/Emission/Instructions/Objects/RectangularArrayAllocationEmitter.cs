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
        new(
            CilOperation.NewBoundedRectangularArray,
            InstructionFamily.ArraysFieldsStatics,
            Emit),
    ];

    private void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        roots.Emit(request, code);
        var arrayType = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var elementType = arrayType.ElementType!;
        var bounded = request.Instruction.Operation == CilOperation.NewBoundedRectangularArray;
        var argumentCount = arrayType.ArrayRank * (bounded ? 2 : 1);
        var firstLengthSlot = request.Stack.Count - argumentCount;
        var scratchOffset = request.Context.ValueLayout.TemporaryOffsets[
            request.Instruction.Offset];
        for (var dimension = 0; dimension < arrayType.ArrayRank; dimension++)
        {
            var lengthLocal = GetStackLocal(
                request.Context,
                firstLengthSlot + (bounded ? dimension * 2 + 1 : dimension),
                CliValueKind.I4);
            Get(code, lengthLocal);
            WriteI32(code, 0);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
            ThrowIf(code, ManagedExceptionKind.Overflow);

            if (bounded)
            {
                var lowerLocal = GetStackLocal(
                    request.Context, firstLengthSlot + dimension * 2, CliValueKind.I4);
                Get(code, lowerLocal);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Signed));
                Get(code, lengthLocal);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Signed));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64((long)int.MaxValue + 1)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanSigned));
                ThrowIf(code, ManagedExceptionKind.ArgumentOutOfRange);

                Get(code, request.Context.ValueFrame);
                addresses.Emit(code, checked(scratchOffset + (arrayType.ArrayRank + dimension) * sizeof(int)));
                addresses.Emit(code, AddressOperation.Add);
                Get(code, lowerLocal);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, 0)));
            }

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
                bounded
                    ? RuntimeImportSymbol.AllocateBoundedRectangularArray
                    : RuntimeImportSymbol.AllocateRectangularArray))));
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
            argumentCount - 1);
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
