// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AddressTakenAggregateProjectionEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsUnsupportedProjectionOperationsBeforeEmission(bool byRef)
    {
        var request = byRef
            ? ManagedByRefRequest(CilOperation.Nop, [CliValueKind.ManagedAddress], false)
            : Request(CilOperation.Nop, [CliValueKind.ManagedAddress], new(0, 0, 4, CliValueKind.I4));
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(new RecordingLayoutProvider());

        Assert.Throws<InvalidOperationException>(() => emitter.TryEmit(request, writer));

        Assert.Empty(writer.ToInstructions());
        Assert.Equal(CliValueKind.ManagedAddress, Assert.Single(request.Stack));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RejectsLoadWhenAnOverlappingLaneIsMissingBeforeEmission(WasmTarget target)
    {
        var request = Request(CilOperation.LoadField, [CliValueKind.ManagedAddress],
            new(0, 4, 8, CliValueKind.I8),
            new(Type(), 12, [new(0, 4, CliValueKind.I4), new(4, 4, CliValueKind.I4)]),
            [40, 41]);
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        Assert.Throws<InvalidOperationException>(() => emitter.TryEmit(request, writer));

        Assert.Empty(writer.ToInstructions());
        Assert.Equal(CliValueKind.ManagedAddress, Assert.Single(request.Stack));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LoadsI4LaneWithoutLinearMemory(WasmTarget target)
    {
        var request = Request(
            CilOperation.LoadField,
            [CliValueKind.ManagedAddress],
            new(0, 0, 4, CliValueKind.I4));
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        Assert.True(emitter.TryEmit(request, writer));

        var instructions = writer.ToInstructions();
        Assert.Equal(CliValueKind.I4, Assert.Single(request.Stack));
        Assert.Equal(WasmOpcodes.LocalGet, instructions[0].Opcode);
        Assert.Equal(40U, instructions[0].Operand.UnsignedValue);
        Assert.Equal(WasmOpcodes.LocalSet, instructions[1].Opcode);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.I32Load or WasmOpcodes.I32Store);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LoadsOverlappingI8FromTwoI4Lanes(WasmTarget target)
    {
        var request = Request(
            CilOperation.LoadField,
            [CliValueKind.ManagedAddress],
            new(0, 4, 8, CliValueKind.I8));
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        Assert.True(emitter.TryEmit(request, writer));

        var instructions = writer.ToInstructions();
        Assert.Equal(CliValueKind.I8, Assert.Single(request.Stack));
        Assert.Equal(41U, instructions[0].Operand.UnsignedValue);
        Assert.Equal(42U, instructions[2].Operand.UnsignedValue);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I64ShiftLeft);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I64Or);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.I64Load or WasmOpcodes.I64Store);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StoresOverlappingI8IntoTwoI4Lanes(WasmTarget target)
    {
        var request = Request(
            CilOperation.StoreField,
            [CliValueKind.ManagedAddress, CliValueKind.I8],
            new(0, 4, 8, CliValueKind.I8));
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        Assert.True(emitter.TryEmit(request, writer));

        var instructions = writer.ToInstructions();
        Assert.Empty(request.Stack);
        Assert.Equal(41U, instructions[2].Operand.UnsignedValue);
        Assert.Equal(42U, instructions[^1].Operand.UnsignedValue);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I64ShiftRightUnsigned);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.I64Load or WasmOpcodes.I64Store);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LoadsDirectI8StorageWithoutLaneComposition(WasmTarget target)
    {
        var request = Request(
            CilOperation.LoadField,
            [CliValueKind.ManagedAddress],
            new(0, 4, 8, CliValueKind.I8),
            MixedStoragePlan(),
            [40, 41]);
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        Assert.True(emitter.TryEmit(request, writer));

        var instructions = writer.ToInstructions();
        Assert.Equal(CliValueKind.I8, Assert.Single(request.Stack));
        Assert.Equal(WasmOpcodes.LocalGet, instructions[0].Opcode);
        Assert.Equal(41U, instructions[0].Operand.UnsignedValue);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.I64ExtendI32Unsigned or
                WasmOpcodes.I64ShiftLeft or WasmOpcodes.I64Or);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StoresDirectI8StorageWithoutLaneSplitting(WasmTarget target)
    {
        var request = Request(
            CilOperation.StoreField,
            [CliValueKind.ManagedAddress, CliValueKind.I8],
            new(0, 4, 8, CliValueKind.I8),
            MixedStoragePlan(),
            [40, 41]);
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        Assert.True(emitter.TryEmit(request, writer));

        var instructions = writer.ToInstructions();
        Assert.Empty(request.Stack);
        Assert.Equal(WasmOpcodes.LocalSet, instructions[^1].Opcode);
        Assert.Equal(41U, instructions[^1].Operand.UnsignedValue);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.I32WrapI64 or
                WasmOpcodes.I64ShiftRightUnsigned);
    }

    [Fact]
    public void IgnoresUnplannedFieldAndValidatesInputs()
    {
        var request = CreateInstructionRequest(
            CilOperation.LoadField,
            [CliValueKind.ManagedAddress]);
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(
            new RecordingLayoutProvider());

        Assert.False(emitter.TryEmit(request, writer));
        Assert.Empty(writer.ToInstructions());
        Assert.Throws<ArgumentNullException>(() => emitter.TryEmit(null!, writer));
        Assert.Throws<ArgumentNullException>(() => emitter.TryEmit(request, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new AddressTakenAggregateProjectionEmitter(null!));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CilOperation.StoreField, 4)]
    [InlineData(WasmTarget.Wasm32, CilOperation.StoreObject, 0)]
    [InlineData(WasmTarget.Wasm64, CilOperation.StoreField, 4)]
    [InlineData(WasmTarget.Wasm64, CilOperation.StoreObject, 0)]
    public void ManagedByRefStoresWriteThroughAndUpdateThePromotedLane(
        WasmTarget target,
        CilOperation operation,
        int expectedMemoryOffset)
    {
        var request = ManagedByRefRequest(
            operation,
            [CliValueKind.ManagedAddress, CliValueKind.I8],
            addressAlreadyProjected: operation == CilOperation.StoreObject);
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        Assert.True(emitter.TryEmit(request, writer));

        var instructions = writer.ToInstructions();
        Assert.Empty(request.Stack);
        Assert.Single(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I64Store &&
            instruction.Operand.Offset == (uint)expectedMemoryOffset);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == 41U);
    }

    [Theory]
    [InlineData(CilOperation.LoadField)]
    [InlineData(CilOperation.LoadObject)]
    public void ManagedByRefLoadsReadThePromotedLaneWithoutMemory(
        CilOperation operation)
    {
        var request = ManagedByRefRequest(
            operation,
            [CliValueKind.ManagedAddress],
            addressAlreadyProjected: operation == CilOperation.LoadObject);
        var writer = new RecordingInstructionWriter();
        var emitter = new AddressTakenAggregateProjectionEmitter(
            new RecordingLayoutProvider());

        Assert.True(emitter.TryEmit(request, writer));

        var instructions = writer.ToInstructions();
        Assert.Equal(CliValueKind.I8, Assert.Single(request.Stack));
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == 41U);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.I64Load or WasmOpcodes.I32Load);
    }

    private static InstructionEmissionRequest Request(
        CilOperation operation,
        IEnumerable<CliValueKind> stack,
        AddressTakenAggregateProjection projection,
        AddressTakenAggregateLocalPlan? localPlan = null,
        ImmutableArray<int> storageLocals = default)
    {
        localPlan ??= AddressTakenAggregateLocalPlan.CreateI4Storage(Type(), 12);
        storageLocals = storageLocals.IsDefault ? [40, 41, 42] : storageLocals;
        var plan = new AddressTakenAggregatePromotionPlan(
            ImmutableDictionary<int, AddressTakenAggregateLocalPlan>.Empty.Add(
                0,
                localPlan),
            ImmutableDictionary<int, AddressTakenAggregateProjection>.Empty.Add(
                0,
                projection),
            []);
        var context = CreateMethodEmissionContext() with
        {
            AddressTakenAggregatePromotions = new(
                plan,
                ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(
                    0,
                    storageLocals)),
        };
        return CreateInstructionRequest(operation, stack, context: context);
    }

    private static AddressTakenAggregateLocalPlan MixedStoragePlan() => new(
        Type(),
        12,
        [
            new(0, 4, CliValueKind.I4),
            new(4, 8, CliValueKind.I8),
        ]);

    private static InstructionEmissionRequest ManagedByRefRequest(
        CilOperation operation,
        IEnumerable<CliValueKind> stack,
        bool addressAlreadyProjected)
    {
        var parameter = new ManagedByRefAggregateParameterPlan(
            Type(),
            12,
            MixedStoragePlan().StorageSlots);
        var projection = new ManagedByRefAggregateProjection(
            0,
            4,
            8,
            CliValueKind.I8,
            addressAlreadyProjected);
        var plan = new ManagedByRefAggregatePromotionPlan(
            ImmutableDictionary<int, ManagedByRefAggregateParameterPlan>.Empty.Add(
                0,
                parameter),
            ImmutableDictionary<int, ManagedByRefAggregateProjection>.Empty.Add(
                0,
                projection));
        var context = CreateMethodEmissionContext() with
        {
            ManagedByRefAggregatePromotions = new(
                plan,
                ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(
                    0,
                    [40, 41])),
        };
        return CreateInstructionRequest(operation, stack, context: context);
    }

    private static CliTypeIdentity Type() => CliTypeIdentity.Named(
        new("aggregate-projection-tests"),
        "Tests",
        "Buffer",
        isValueType: true);
}
