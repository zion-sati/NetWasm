using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.Planning;

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
    IArrayLengthAdapter lengths,
    IArrayIndexAdapter indexes,
    IArrayElementAddressEmitter elementAddresses,
    IStaticFieldLayoutProvider staticFields) :
    InstructionCommandProvider
{
    // An initializer up to this size is stored as constants, eight bytes at a time. A larger one is
    // one memory.copy from its field's static data: Wasmtime runs memory.copy out of line, which a
    // few stores beat.
    private const int ConstantStoreLimit = 64;

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
        EmitAllocationFailureCheck(code, arrayLocal, request.Context.ManagedThrowFunction);
        request.Stack[lengthSlot] = CliValueKind.ManagedReference;
    }

    private void EmitInitializeArrayData(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arraySlot = request.Stack.Count - 1;
        var arrayLocal = GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference);
        var initializer = (CilOperand.ArrayInitializerData)request.Instruction.Operand;
        var data = initializer.Value;
        if (data.Length > ConstantStoreLimit)
        {
            EmitArrayData(code, arrayLocal);
            addresses.Emit(code, staticFields.GetStaticFieldLayout(initializer.Field).Address);
            addresses.Emit(code, data.Length);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed,
                WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        }
        else
        {
            for (var offset = 0; offset < data.Length;)
            {
                var width = (data.Length - offset) switch { >= 8 => 8, >= 4 => 4, >= 2 => 2, _ => 1 };
                var value = 0L;
                for (var index = width - 1; index >= 0; index--)
                {
                    value = (value << 8) | data[offset + index];
                }
                EmitArrayData(code, arrayLocal);
                code.Write(width == 8
                    ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value))
                    : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(unchecked((int)value))));
                code.Write(WasmInstruction.WithOperand(width switch
                {
                    8 => WasmOpcodes.I64Store,
                    4 => WasmOpcodes.I32Store,
                    2 => WasmOpcodes.I32Store16,
                    _ => WasmOpcodes.I32Store8,
                }, WasmInstructionOperand.Memory(0, (uint)offset)));
                offset += width;
            }
        }
        request.Stack.RemoveAt(arraySlot);
    }

    private void EmitArrayData(IWasmInstructionWriter code, int arrayLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
        ManagedMemoryEmitter.EmitReferenceLoad(
            code,
            layouts.Target,
            objects.ArrayDataPointerOffset);
    }

    private void EmitLength(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arraySlot = request.Stack.Count - 1;
        var arrayLocal = GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference);
        if (!request.Context.ArrayMetadata.Plan.CheckedAccesses.Contains(request.Instruction.Offset))
        {
            EmitNullCheck(code, arrayLocal, request.Context.ManagedThrowFunction);
        }
        if (request.Context.ArrayMetadata.Plan.Accesses.TryGetValue(request.Instruction.Offset, out var slot))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)request.Context.ArrayMetadata.Slots[slot].Length)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(objects.ArrayLengthOffset))));
        }
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
        var indexLocal = indexes.Adapt(
            request,
            code,
            arrayLocal,
            arraySlot + 1);
        elementAddresses.Emit(
            request,
            code,
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
        var indexLocal = indexes.Adapt(
            request,
            code,
            arrayLocal,
            arraySlot + 1);
        var valueLocal = GetStackLocal(
            request.Context,
            arraySlot + 2,
            CliValueKind.ManagedReference);
        EmitReferenceStoreTypeCheck(request, code, arrayLocal, valueLocal);
        elementAddresses.Emit(
            request,
            code,
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

    private void EmitReferenceStoreTypeCheck(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int arrayLocal,
        int valueLocal)
    {
        if (request.Context.ExactArrayStores.Contains(request.Instruction.Offset))
        {
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        // As .NET's store helper does, accept a value of exactly the element
        // type, or any value into an object[], before the hierarchy check.
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        EmitElementTypeId(code, arrayLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32NotEqual));
        if (typeLayouts.GetObjectLayout(ObjectType, out var objectLayout))
        {
            EmitElementTypeId(code, arrayLocal);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(objectLayout.TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32NotEqual));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        EmitElementTypeId(code, arrayLocal);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.IsAssignable)))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.ArrayTypeMismatch, request.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static readonly CliTypeIdentity ObjectType =
        CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, isValueType: false);

    private void EmitElementTypeId(IWasmInstructionWriter code, int arrayLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(objects.ArrayElementTypeIdOffset))));
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
        var indexLocal = indexes.Adapt(
            request,
            code,
            arrayLocal,
            arraySlot + 1);
        if (elementType.StackKind == CliValueKind.ValueType &&
            request.Context.ValueLayout.DirectValueLoadOffsets.Contains(
                request.Instruction.Offset))
        {
            elementAddresses.Emit(
                request,
                code,
                arrayLocal,
                indexLocal,
                elementSize);
        }
        else if (elementType.StackKind == CliValueKind.ValueType)
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
            elementAddresses.Emit(
                request,
                code,
                arrayLocal,
                indexLocal,
                elementSize);
            SmallBlockEmitter.EmitKnownSizeCopy(code, request.Context, elementSize,
                writer => addresses.Emit(writer, elementSize));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        }
        else
        {
            elementAddresses.Emit(
                request,
                code,
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
        var indexLocal = indexes.Adapt(
            request,
            code,
            arrayLocal,
            arraySlot + 1);
        if (elementType.StackKind == CliValueKind.ManagedReference &&
            !HasReadonlyPrefix(request))
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)arrayLocal)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(
                    2,
                    (uint)objects.ArrayElementTypeIdOffset)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(
                    typeLayouts.GetObjectLayout(elementType).TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.ArrayTypeMismatch, request.Context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        elementAddresses.Emit(
            request,
            code,
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
        var indexLocal = indexes.Adapt(
            request,
            code,
            arrayLocal,
            arraySlot + 1);
        var valueLocal = GetStackLocal(
            request.Context,
            arraySlot + 2,
            elementType.StackKind);
        if (elementType.StackKind == CliValueKind.ManagedReference)
        {
            EmitReferenceStoreTypeCheck(request, code, arrayLocal, valueLocal);
        }
        elementAddresses.Emit(
            request,
            code,
            arrayLocal,
            indexLocal,
            elementSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        if (elementType.StackKind == CliValueKind.ValueType)
        {
            SmallBlockEmitter.EmitKnownSizeCopy(code, request.Context, elementSize,
                writer => addresses.Emit(writer, elementSize));
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

    private int GetElementSize(CliTypeIdentity elementType) =>
        elementType.StackKind == CliValueKind.ManagedReference
            ? layouts.Target.ObjectReferenceSize
            : values.GetValueLayout(elementType).Size;

    private void EmitNullCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitAllocationFailureCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, throwFunction);
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
