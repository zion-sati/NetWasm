// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AddressTakenAggregateSynchronizationEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SynchronizesAllLanesAroundWholeLocalTransfers(WasmTarget target)
    {
        var request = Request();
        var emitter = new AddressTakenAggregateSynchronizationEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        var beforeLoad = new RecordingInstructionWriter();
        emitter.Emit(request, beforeLoad, 1,
            AddressTakenValueSynchronizationPhase.BeforeCall);
        Assert.Equal(3, beforeLoad.ToInstructions().Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32Store));

        var afterStore = new RecordingInstructionWriter();
        emitter.Emit(request, afterStore, 1,
            AddressTakenValueSynchronizationPhase.AfterCall);
        Assert.Equal(3, afterStore.ToInstructions().Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32Load));
        Assert.Equal([40U, 41U, 42U], afterStore.ToInstructions()
            .Where(instruction => instruction.Opcode == WasmOpcodes.LocalSet)
            .Select(instruction => instruction.Operand.UnsignedValue)
            .ToArray());
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StoresAllLanesBeforeCallAndReloadsWrittenLanesAfterReturn(
        WasmTarget target)
    {
        var request = Request();
        var emitter = new AddressTakenAggregateSynchronizationEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));
        var synchronization = new AddressTakenAggregateCallSynchronization([1], [1]);

        var before = new RecordingInstructionWriter();
        emitter.Emit(
            request,
            before,
            synchronization,
            AddressTakenValueSynchronizationPhase.BeforeCall);
        var beforeInstructions = before.ToInstructions();
        Assert.Equal(3, beforeInstructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32Store));
        Assert.Equal(3, beforeInstructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == 8U));
        Assert.Contains(beforeInstructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == 40U);
        Assert.Contains(beforeInstructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == 42U);

        var after = new RecordingInstructionWriter();
        emitter.Emit(
            request,
            after,
            synchronization,
            AddressTakenValueSynchronizationPhase.AfterCall);
        var afterInstructions = after.ToInstructions();
        Assert.Equal(3, afterInstructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32Load));
        Assert.Equal([40U, 41U, 42U], afterInstructions
            .Where(instruction => instruction.Opcode == WasmOpcodes.LocalSet)
            .Select(instruction => instruction.Operand.UnsignedValue)
            .ToArray());
    }

    [Fact]
    public void ReadOnlyCallDoesNotReloadAndContractInputsAreValidated()
    {
        var request = Request();
        var emitter = new AddressTakenAggregateSynchronizationEmitter(
            new RecordingLayoutProvider());
        var synchronization = new AddressTakenAggregateCallSynchronization([1], []);
        var writer = new RecordingInstructionWriter();

        emitter.Emit(
            request,
            writer,
            synchronization,
            AddressTakenValueSynchronizationPhase.AfterCall);

        Assert.Empty(writer.ToInstructions());
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            null!, writer, synchronization, AddressTakenValueSynchronizationPhase.BeforeCall));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            request, null!, synchronization, AddressTakenValueSynchronizationPhase.BeforeCall));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            request, writer, null!, AddressTakenValueSynchronizationPhase.BeforeCall));
        Assert.Throws<ArgumentNullException>(() =>
            new AddressTakenAggregateSynchronizationEmitter(null!));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SynchronizesMixedI4AndI8Storage(WasmTarget target)
    {
        var request = Request(mixedStorage: true);
        var emitter = new AddressTakenAggregateSynchronizationEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));
        var synchronization = new AddressTakenAggregateCallSynchronization([1], [1]);

        var before = new RecordingInstructionWriter();
        emitter.Emit(
            request,
            before,
            synchronization,
            AddressTakenValueSynchronizationPhase.BeforeCall);
        var beforeInstructions = before.ToInstructions();
        Assert.Single(beforeInstructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Store);
        Assert.Single(beforeInstructions, instruction =>
            instruction.Opcode == WasmOpcodes.I64Store);

        var after = new RecordingInstructionWriter();
        emitter.Emit(
            request,
            after,
            synchronization,
            AddressTakenValueSynchronizationPhase.AfterCall);
        var afterInstructions = after.ToInstructions();
        Assert.Single(afterInstructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load);
        Assert.Single(afterInstructions, instruction =>
            instruction.Opcode == WasmOpcodes.I64Load);
        Assert.Equal([40U, 41U], afterInstructions
            .Where(instruction => instruction.Opcode == WasmOpcodes.LocalSet)
            .Select(instruction => instruction.Operand.UnsignedValue)
            .ToArray());
    }

    private static InstructionEmissionRequest Request(bool mixedStorage = false)
    {
        var type = CliTypeIdentity.Named(
            new("aggregate-synchronization-tests"),
            "Tests",
            "Buffer",
            isValueType: true);
        var localPlan = mixedStorage
            ? new AddressTakenAggregateLocalPlan(
                type,
                12,
                [
                    new(0, 4, CliValueKind.I4),
                    new(4, 8, CliValueKind.I8),
                ])
            : AddressTakenAggregateLocalPlan.CreateI4Storage(type, 12);
        var storageLocals = mixedStorage
            ? ImmutableArray.Create(40, 41)
            : [40, 41, 42];
        var plan = new AddressTakenAggregatePromotionPlan(
            ImmutableDictionary<int, AddressTakenAggregateLocalPlan>.Empty.Add(
                1,
                localPlan),
            [],
            []);
        var context = CreateMethodEmissionContext() with
        {
            LocalBase = 7,
            AddressTakenAggregatePromotions = new(
                plan,
                ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(
                    1,
                    storageLocals)),
        };
        return CreateInstructionRequest(
            CilOperation.Call,
            context: context);
    }
}
