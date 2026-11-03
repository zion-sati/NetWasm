using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal sealed class ArrayMetadataSnapshotEmitter(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects,
    IRectangularArrayLayoutProvider rectangularLayouts,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : IArrayMetadataSnapshotEmitter
{
    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, StructuredBlockId loopCondition)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        if (context.ArrayMetadata.Plan.LoopEntries.TryGetValue(loopCondition, out var slots))
        {
            foreach (var slot in slots) Emit(code, context, slot);
        }
        if (!context.ArrayMetadata.Plan.EntryNullChecks.TryGetValue(loopCondition, out var checks)) return;
        // The loop's condition would make these checks first on its first evaluation.
        foreach (var slot in checks)
        {
            Get(code, Reference(context, slot));
            addresses.Emit(code, AddressOperation.EqualZero);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.NullReference, context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        foreach (var slot in context.ArrayMetadata.Plan.Slots)
        {
            if (slot.IsArgument) Emit(code, context, slot);
        }
    }

    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, ArrayReferenceSlot slot)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        if (context.ArrayMetadata.RectangularSlots.TryGetValue(slot, out var rectangular))
        {
            Emit(code, Reference(context, slot), rectangular);
            return;
        }
        if (!context.ArrayMetadata.Slots.TryGetValue(slot, out var snapshot)) return;
        var reference = Reference(context, slot);
        // Preparation must not throw, even for an unused null argument or a zero-trip loop.
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        Set(code, snapshot.Length);
        addresses.Emit(code, 0);
        Set(code, snapshot.Data);
        Get(code, reference);
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Get(code, reference);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.ArrayLengthOffset)));
        Set(code, snapshot.Length);
        Get(code, reference);
        ManagedMemoryEmitter.EmitArrayData(code, layouts.Target, context.InlineArrayData);
        Set(code, snapshot.Data);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // An array's shape and data never change once it is allocated.
    private void Emit(IWasmInstructionWriter code, int reference, RectangularArraySnapshotLocals snapshot)
    {
        var shape = rectangularLayouts.Provide();
        foreach (var dimension in snapshot.Dimensions)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
            Set(code, dimension.LowerBound);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
            Set(code, dimension.Length);
        }
        addresses.Emit(code, 0);
        Set(code, snapshot.Data);
        Get(code, reference);
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        for (var dimension = 0; dimension < snapshot.Dimensions.Length; dimension++)
        {
            Get(code, reference);
            ManagedMemoryEmitter.EmitReferenceLoad(code, layouts.Target, shape.ShapePointerOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(
                2, (uint)shape.LowerBoundOffset(snapshot.Dimensions.Length, dimension))));
            Set(code, snapshot.Dimensions[dimension].LowerBound);
            Get(code, reference);
            ManagedMemoryEmitter.EmitReferenceLoad(code, layouts.Target, shape.ShapePointerOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(
                2, (uint)(dimension * shape.DimensionSize + shape.DimensionLengthOffset))));
            Set(code, snapshot.Dimensions[dimension].Length);
        }
        Get(code, reference);
        ManagedMemoryEmitter.EmitReferenceLoad(code, layouts.Target, objects.ArrayDataPointerOffset);
        Set(code, snapshot.Data);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static int Reference(MethodEmissionContext context, ArrayReferenceSlot slot) =>
        checked(slot.Index + (slot.IsArgument ? context.ParameterOffset : context.LocalBase));

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
