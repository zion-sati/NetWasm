using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class RectangularArrayElementAddressEmitter(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects,
    IRectangularArrayLayoutProvider rectangularLayouts,
    IAddressInstructionEmitter addresses,
    IValueLayoutProvider values,
    ITypeLayoutProvider typeLayouts,
    IImplicitExceptionEmitter exceptions,
    IRuntimeImportResolver runtimeImports) : IRectangularArrayElementAddressEmitter
{
    public void Emit(
        RectangularArrayElementAddressRequest request,
        IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var instruction = request.Instruction;
        var arrayType = GetArrayType(instruction);
        var elementType = arrayType.ElementType!;
        var arrayLocal = GetStackLocal(
            instruction.Context,
            request.ArraySlot,
            CliValueKind.ManagedReference);
        EmitNullCheck(code, arrayLocal);

        if (instruction.Instruction.Operation ==
                CilOperation.LoadRectangularArrayElementAddress &&
            elementType.StackKind == CliValueKind.ManagedReference &&
            !HasReadonlyPrefix(instruction))
        {
            Get(code, arrayLocal);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            WriteI32(code, typeLayouts.GetObjectLayout(arrayType).TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            ThrowIf(code, ManagedExceptionKind.ArrayTypeMismatch);
        }

        if (arrayType.ArrayRank == 1)
        {
            EmitRankOneAddress(
                instruction,
                code,
                arrayLocal,
                request.ArraySlot,
                elementType);
            return;
        }

        var shape = rectangularLayouts.Provide();
        var accumulator = instruction.Context.NumericTemporaryI4;
        WriteI32(code, 0);
        Set(code, accumulator);
        for (var dimension = 0; dimension < arrayType.ArrayRank; dimension++)
        {
            var indexLocal = GetStackLocal(
                instruction.Context,
                request.ArraySlot + dimension + 1,
                CliValueKind.I4);
            NormalizeIndex(code, arrayLocal, indexLocal, dimension);

            Get(code, indexLocal);
            EmitShapeFieldAddress(code, arrayLocal, shape, dimension);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(
                    2,
                    (uint)shape.DimensionLengthOffset)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
            ThrowIf(code, ManagedExceptionKind.IndexOutOfRange);

            Get(code, accumulator);
            Get(code, indexLocal);
            EmitShapeFieldAddress(code, arrayLocal, shape, dimension);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(
                    2,
                    (uint)shape.DimensionStrideOffset)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
            Set(code, accumulator);
        }

        Get(code, arrayLocal);
        ManagedMemoryEmitter.EmitReferenceLoad(
            code,
            layouts.Target,
            objects.ArrayDataPointerOffset);
        Get(code, accumulator);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        addresses.Emit(code, GetElementSize(elementType));
        code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64
            ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        addresses.Emit(code, AddressOperation.Add);
    }

    private void EmitRankOneAddress(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int arrayLocal,
        int arraySlot,
        CliTypeIdentity elementType)
    {
        var indexLocal = GetStackLocal(
            request.Context,
            arraySlot + 1,
            CliValueKind.I4);
        NormalizeIndex(code, arrayLocal, indexLocal, 0);

        Get(code, indexLocal);
        Get(code, arrayLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(
                2,
                (uint)objects.ArrayLengthOffset)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        ThrowIf(code, ManagedExceptionKind.IndexOutOfRange);

        Get(code, arrayLocal);
        ManagedMemoryEmitter.EmitReferenceLoad(
            code,
            layouts.Target,
            objects.ArrayDataPointerOffset);
        Get(code, indexLocal);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        addresses.Emit(code, GetElementSize(elementType));
        code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64
            ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        addresses.Emit(code, AddressOperation.Add);
    }

    private void NormalizeIndex(IWasmInstructionWriter code, int array, int index, int dimension)
    {
        Get(code, index);
        Get(code, array);
        WriteI32(code, dimension);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.ArrayGetLowerBound))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        Set(code, index);
    }

    private static bool HasReadonlyPrefix(InstructionEmissionRequest request)
    {
        var instructions = request.Header.Instructions;
        for (var index = 1; index < instructions.Length; index++)
        {
            if (instructions[index].Offset == request.Instruction.Offset)
            {
                return instructions[index - 1].Operation == CilOperation.Readonly;
            }
        }

        return false;
    }

    private void EmitShapeFieldAddress(
        IWasmInstructionWriter code,
        int arrayLocal,
        RectangularArrayLayout shape,
        int dimension)
    {
        Get(code, arrayLocal);
        ManagedMemoryEmitter.EmitReferenceLoad(
            code,
            layouts.Target,
            shape.ShapePointerOffset);
        addresses.Emit(code, checked(dimension * shape.DimensionSize));
        addresses.Emit(code, AddressOperation.Add);
    }

    private void EmitNullCheck(IWasmInstructionWriter code, int arrayLocal)
    {
        Get(code, arrayLocal);
        addresses.Emit(code, AddressOperation.EqualZero);
        ThrowIf(code, ManagedExceptionKind.NullReference);
    }

    private void ThrowIf(IWasmInstructionWriter code, ManagedExceptionKind kind)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, kind);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private int GetElementSize(CliTypeIdentity elementType) =>
        elementType.StackKind == CliValueKind.ManagedReference
            ? layouts.Target.ObjectReferenceSize
            : values.GetValueLayout(elementType).Size;

    private static CliTypeIdentity GetArrayType(InstructionEmissionRequest request) =>
        request.Instruction.Operand is CilOperand.TypeIdentity { Value.Shape: CliTypeShape.Array } type
            ? type.Value
            : throw new InvalidOperationException(
                "rectangular-array instruction has no array type operand");

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
