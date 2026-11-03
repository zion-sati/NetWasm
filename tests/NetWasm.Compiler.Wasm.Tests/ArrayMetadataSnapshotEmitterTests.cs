using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayMetadataSnapshotEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LoopEntryUsesExistingNullSafeSnapshotContract(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new[] { new ArrayMetadataSnapshotEmitter(layouts, layouts, new RectangularArrayLayoutProvider(layouts, layouts), CreateAddressInstructions(layouts),
                new ImplicitExceptionEmitter(layouts)) }
            .Cast<IArrayMetadataSnapshotEmitter>().Single();
        var slot = new ArrayReferenceSlot(false, 0);
        var plan = new ArrayMetadataCachingPlan([slot], [])
        {
            LoopEntries = ImmutableDictionary<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>>.Empty
                .Add(new(7), [slot]),
        };
        var context = CreateMethodEmissionContext() with
        {
            ArrayMetadata = new(plan,
                ImmutableDictionary<ArrayReferenceSlot, ArrayMetadataSnapshotLocals>.Empty.Add(slot, new(30, 31))),
            InlineArrayData = true,
        };
        var expected = new RecordingInstructionWriter();
        var actual = new RecordingInstructionWriter();

        emitter.Emit(expected, context, slot);
        emitter.Emit(actual, context, new StructuredBlockId(7));

        Assert.Equal(expected.ToArray(), actual.ToArray());
        Assert.Single(actual.ToInstructions(), instruction => instruction.Opcode == WasmOpcodes.If);
        // The snapshot loads the length; the elements sit inline after the
        // array's fields, so it adds their offset rather than loading a pointer.
        var layout = WasmTargetLayout.For(target);
        Assert.Contains(actual.ToInstructions(), static instruction => instruction.Opcode == WasmOpcodes.I32Load);
        Assert.DoesNotContain(actual.ToInstructions(), static instruction => instruction.Opcode == WasmOpcodes.I64Load);
        Assert.Contains(actual.ToInstructions(), instruction =>
            instruction.Opcode is WasmOpcodes.I32Constant or WasmOpcodes.I64Constant &&
            (instruction.Operand?.SignedValue == layout.ArrayInlineDataOffset ||
                instruction.Operand?.Signed64Value == layout.ArrayInlineDataOffset));
        var absent = new RecordingInstructionWriter();
        emitter.Emit(absent, context, new StructuredBlockId(8));
        Assert.Empty(absent.ToInstructions());
    }

    // The snapshot reads each lower bound and length from the shape, and the
    // data, only for a non-null array; a null one leaves them zero.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RectangularSnapshotReadsEachDimensionAndTheData(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new ArrayMetadataSnapshotEmitter(layouts, layouts, new RectangularArrayLayoutProvider(layouts, layouts),
            CreateAddressInstructions(layouts), new ImplicitExceptionEmitter(layouts));
        var slot = new ArrayReferenceSlot(true, 0);
        var context = CreateMethodEmissionContext() with
        {
            ArrayMetadata = new(new ArrayMetadataCachingPlan([slot], []), [])
            {
                RectangularSlots = ImmutableDictionary<ArrayReferenceSlot, RectangularArraySnapshotLocals>.Empty
                    .Add(slot, new([new(30, 31), new(32, 33), new(34, 35)], 36)),
            },
        };
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, context);

        var instructions = code.ToInstructions();
        var shape = layouts.Provide();
        var guard = instructions.ToList().FindIndex(instruction => instruction.Opcode == WasmOpcodes.If);
        Assert.Single(instructions, instruction => instruction.Opcode == WasmOpcodes.If);
        for (var dimension = 0; dimension < 3; dimension++)
        {
            Assert.Contains(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, (uint)shape.LowerBoundOffset(3, dimension))), instructions[guard..]);
            Assert.Contains(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, (uint)(dimension * shape.DimensionSize))), instructions[guard..]);
        }
        foreach (var local in new[] { 30, 31, 32, 33, 34, 35, 36 })
        {
            var set = WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local));
            Assert.Contains(set, instructions[..guard]);
            Assert.Contains(set, instructions[guard..]);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false, true)]
    [InlineData(WasmTarget.Wasm64, false, true)]
    [InlineData(WasmTarget.Wasm32, true, true)]
    // A loop inside another uses the outer loop's snapshot but still makes its checks.
    [InlineData(WasmTarget.Wasm32, false, false)]
    public void LoopEntryChecksItsHoistedArraysAfterTheSnapshots(WasmTarget target, bool argument, bool snapshotted)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new ArrayMetadataSnapshotEmitter(layouts, layouts, new RectangularArrayLayoutProvider(layouts, layouts), CreateAddressInstructions(layouts),
            new ImplicitExceptionEmitter(layouts));
        var slot = new ArrayReferenceSlot(argument, 0);
        var plan = new ArrayMetadataCachingPlan([slot], [])
        {
            LoopEntries = snapshotted
                ? ImmutableDictionary<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>>.Empty.Add(new(7), [slot])
                : [],
            EntryNullChecks = ImmutableDictionary<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>>.Empty.Add(new(7), [slot]),
        };
        var context = CreateMethodEmissionContext() with
        {
            ArrayMetadata = new(plan,
                ImmutableDictionary<ArrayReferenceSlot, ArrayMetadataSnapshotLocals>.Empty.Add(slot, new(30, 31))),
        };
        var snapshot = new RecordingInstructionWriter();
        var entry = new RecordingInstructionWriter();

        if (snapshotted) emitter.Emit(snapshot, context, slot);
        emitter.Emit(entry, context, new StructuredBlockId(7));

        var instructions = entry.ToInstructions();
        var check = instructions.Skip(snapshot.ToInstructions().Length).ToArray();
        Assert.Equal(snapshot.ToInstructions().Select(instruction => instruction.Opcode),
            instructions.Take(snapshot.ToInstructions().Length).Select(instruction => instruction.Opcode));
        Assert.Equal(WasmOpcodes.LocalGet, check[0].Opcode);
        Assert.Equal((uint)(argument ? context.ParameterOffset : context.LocalBase), check[0].Operand.UnsignedValue);
        Assert.Equal(WasmOpcodes.If, check[2].Opcode);
        Assert.Equal(WasmOpcodes.End, check[^1].Opcode);
        Assert.True(ContainsManagedThrow(check));
    }
}
