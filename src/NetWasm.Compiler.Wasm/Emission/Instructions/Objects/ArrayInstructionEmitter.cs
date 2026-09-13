using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class ArrayInstructionEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    ICilTypeOperandResolver types,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots,
    IArrayLengthAdapter lengths) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.NewArray, EmitNewArray),
        Command(CilOperation.LoadArrayLength, EmitLength),
        Command(CilOperation.LoadArrayElementReference, EmitLoadReference),
        Command(CilOperation.StoreArrayElementReference, EmitStoreReference),
        Command(CilOperation.LoadArrayElement, EmitLoadElement),
        Command(CilOperation.LoadArrayElementAddress, EmitLoadElementAddress),
        Command(CilOperation.StoreArrayElement, EmitStoreElement),
        Command(CilOperation.InitializeArrayData, EmitInitializeArrayData),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.ArraysFieldsStatics,
        emit);

    private void EmitNewArray(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        roots.Emit(request, code);
        var lengthSlot = request.Stack.Count - 1;
        var lengthLocal = lengths.Adapt(request, code, lengthSlot);
        var elementType = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var arrayType = CliTypeIdentity.SzArray(elementType);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(lengthLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.GetObjectLayout(arrayType).TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.GetObjectLayout(elementType).TypeId)));
        if (elementType.StackKind != CliValueKind.ManagedReference)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(values.GetValueLayout(elementType).Size)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.AllocateValueArray)))));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.AllocateReferenceArray)))));
        }
        var arrayLocal = GetStackLocal(
            request.Context,
            lengthSlot,
            CliValueKind.ManagedReference);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
        EmitAllocationFailureCheck(code, arrayLocal);
        request.Stack[lengthSlot] = CliValueKind.ManagedReference;
    }

    private void EmitInitializeArrayData(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arraySlot = request.Stack.Count - 1;
        var arrayLocal = GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference);
        var data = ((CilOperand.ByteData)request.Instruction.Operand).Value;
        for (var index = 0; index < data.Length; index++)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
            ManagedMemoryEmitter.EmitReferenceLoad(
                code,
                layouts.Target,
                objects.ArrayDataPointerOffset);
            if (index != 0)
            {
                addresses.Emit(code, index);
                addresses.Emit(code, AddressOperation.Add);
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(data[index])));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store8, WasmInstructionOperand.Memory(0, (uint)(0))));
        }
        request.Stack.RemoveAt(arraySlot);
    }

    private void EmitLength(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arraySlot = request.Stack.Count - 1;
        var arrayLocal = GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference);
        EmitNullCheck(code, arrayLocal);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(objects.ArrayLengthOffset))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.I4)))));
        request.Stack[arraySlot] = CliValueKind.I4;
    }

    private void EmitLoadReference(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arraySlot = request.Stack.Count - 2;
        var arrayLocal = GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference);
        var indexLocal = GetStackLocal(
            request.Context,
            arraySlot + 1,
            CliValueKind.I4);
        EmitBoundsCheck(code, arrayLocal, indexLocal);
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code,
            layouts.Target,
            arrayLocal,
            indexLocal,
            layouts.Target.ObjectReferenceSize);
        ManagedMemoryEmitter.EmitLoadBySize(
            code,
            layouts.Target,
            0,
            layouts.Target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference)))));
        request.Stack.RemoveAt(arraySlot + 1);
        request.Stack[arraySlot] = CliValueKind.ManagedReference;
    }

    private void EmitStoreReference(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arraySlot = request.Stack.Count - 3;
        var arrayLocal = GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference);
        var indexLocal = GetStackLocal(
            request.Context,
            arraySlot + 1,
            CliValueKind.I4);
        var valueLocal = GetStackLocal(
            request.Context,
            arraySlot + 2,
            CliValueKind.ManagedReference);
        EmitBoundsCheck(code, arrayLocal, indexLocal);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(objects.ArrayElementTypeIdOffset))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.IsAssignable)))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.ArrayTypeMismatch);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code,
            layouts.Target,
            arrayLocal,
            indexLocal,
            layouts.Target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            0,
            layouts.Target.ObjectReferenceSize);
        request.Stack.RemoveRange(arraySlot, 3);
    }

    private void EmitLoadElement(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arraySlot = request.Stack.Count - 2;
        var elementType = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var elementSize = GetElementSize(elementType);
        var arrayLocal = GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference);
        var indexLocal = GetStackLocal(
            request.Context,
            arraySlot + 1,
            CliValueKind.I4);
        EmitBoundsCheck(code, arrayLocal, indexLocal);
        if (elementType.StackKind == CliValueKind.ValueType)
        {
            var offset = request.Context.ValueLayout.TemporaryOffsets[
                request.Instruction.Offset];
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ValueFrame))));
            if (offset != 0)
            {
                addresses.Emit(code, offset);
                addresses.Emit(code, AddressOperation.Add);
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            ManagedMemoryEmitter.EmitArrayElementAddress(
                code,
                layouts.Target,
                arrayLocal,
                indexLocal,
                elementSize);
            addresses.Emit(code, elementSize);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        }
        else
        {
            ManagedMemoryEmitter.EmitArrayElementAddress(
                code,
                layouts.Target,
                arrayLocal,
                indexLocal,
                elementSize);
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                layouts.Target,
                0,
                elementType,
                elementSize);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            arraySlot,
            elementType.StackKind)))));
        request.Stack.RemoveAt(arraySlot + 1);
        request.Stack[arraySlot] = elementType.StackKind;
    }

    private void EmitLoadElementAddress(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arraySlot = request.Stack.Count - 2;
        var elementType = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var elementSize = GetElementSize(elementType);
        var arrayLocal = GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference);
        var indexLocal = GetStackLocal(
            request.Context,
            arraySlot + 1,
            CliValueKind.I4);
        EmitBoundsCheck(code, arrayLocal, indexLocal);
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code,
            layouts.Target,
            arrayLocal,
            indexLocal,
            elementSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedAddress)))));
        request.Stack.RemoveAt(arraySlot + 1);
        request.Stack[arraySlot] = CliValueKind.ManagedAddress;
    }

    private void EmitStoreElement(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arraySlot = request.Stack.Count - 3;
        var elementType = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var elementSize = GetElementSize(elementType);
        var arrayLocal = GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference);
        var indexLocal = GetStackLocal(
            request.Context,
            arraySlot + 1,
            CliValueKind.I4);
        var valueLocal = GetStackLocal(
            request.Context,
            arraySlot + 2,
            elementType.StackKind);
        EmitBoundsCheck(code, arrayLocal, indexLocal);
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code,
            layouts.Target,
            arrayLocal,
            indexLocal,
            elementSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        if (elementType.StackKind == CliValueKind.ValueType)
        {
            addresses.Emit(code, elementSize);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        }
        else
        {
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                elementType,
                elementSize);
        }
        request.Stack.RemoveRange(arraySlot, 3);
    }

    private int GetElementSize(CliTypeIdentity elementType) =>
        elementType.StackKind == CliValueKind.ManagedReference
            ? layouts.Target.ObjectReferenceSize
            : values.GetValueLayout(elementType).Size;

    private void EmitBoundsCheck(
        IWasmInstructionWriter code,
        int arrayLocal,
        int indexLocal)
    {
        EmitNullCheck(code, arrayLocal);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(indexLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.IndexOutOfRange);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(indexLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(objects.ArrayLengthOffset))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.IndexOutOfRange);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitNullCheck(IWasmInstructionWriter code, int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitAllocationFailureCheck(IWasmInstructionWriter code, int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
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
