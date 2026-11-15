// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AddressTakenScalarSynchronizationEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptySynchronizationDoesNotEmitOrChangeTheStack(bool afterCall)
    {
        var emitter = new AddressTakenScalarSynchronizationEmitter(new RecordingLayoutProvider());
        var request = CreateInstructionRequest(CilOperation.Call, [CliValueKind.I4]);
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer, new([], []), afterCall
            ? AddressTakenValueSynchronizationPhase.AfterCall : AddressTakenValueSynchronizationPhase.BeforeCall);

        Assert.Empty(writer.ToInstructions());
        Assert.Equal(CliValueKind.I4, Assert.Single(request.Stack));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Store, WasmOpcodes.I32Load)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I32Store, WasmOpcodes.I32Load)]
    public void StoresBeforeCallAndReloadsAfterNormalReturn(
        WasmTarget target,
        uint storeOpcode,
        uint loadOpcode)
    {
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var plan = new AddressTakenScalarPromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, type),
            []);
        var context = CreateMethodEmissionContext() with
        {
            LocalBase = 7,
            AddressTakenScalarPromotions = new(
                plan,
                ImmutableDictionary<int, int>.Empty.Add(0, 41)),
        };
        var body = new CilMethodBody(
            new FakeProgram().GetMethod(EntryKey),
            1,
            [CliValueKind.I4],
            [])
        {
            LocalSignatureTypes = [type],
        };
        var request = new InstructionEmissionRequest(
            Header(body),
            I(3, CilOperation.Call, new CilOperand.Entity(EntryKey)),
            [],
            context,
            CreateInstructionModuleTarget(new FakeProgram()));
        var synchronization = new AddressTakenScalarCallSynchronization([0], [0]);
        var emitter = new AddressTakenScalarSynchronizationEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        var before = new RecordingInstructionWriter();
        emitter.Emit(
            request,
            before,
            synchronization,
            AddressTakenValueSynchronizationPhase.BeforeCall);
        var beforeInstructions = before.ToInstructions();
        Assert.Equal(WasmOpcodes.LocalGet, beforeInstructions[0].Opcode);
        Assert.Equal(41u, beforeInstructions[0].Operand.UnsignedValue);
        Assert.Equal(WasmOpcodes.LocalGet, beforeInstructions[1].Opcode);
        Assert.Equal(7u, beforeInstructions[1].Operand.UnsignedValue);
        Assert.Equal(storeOpcode, beforeInstructions[2].Opcode);

        var after = new RecordingInstructionWriter();
        emitter.Emit(
            request,
            after,
            synchronization,
            AddressTakenValueSynchronizationPhase.AfterCall);
        var afterInstructions = after.ToInstructions();
        Assert.Equal(WasmOpcodes.LocalGet, afterInstructions[0].Opcode);
        Assert.Equal(41u, afterInstructions[0].Operand.UnsignedValue);
        Assert.Equal(loadOpcode, afterInstructions[1].Opcode);
        Assert.Equal(WasmOpcodes.LocalSet, afterInstructions[2].Opcode);
        Assert.Equal(7u, afterInstructions[2].Operand.UnsignedValue);
    }

    [Fact]
    public void ValidatesItsContractInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new AddressTakenScalarSynchronizationEmitter(null!));
        var emitter = new AddressTakenScalarSynchronizationEmitter(
            new RecordingLayoutProvider());
        var synchronization = new AddressTakenScalarCallSynchronization([], []);
        var request = CreateInstructionRequest(CilOperation.Call);
        var code = new RecordingInstructionWriter();

        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            null!,
            code,
            synchronization,
            AddressTakenValueSynchronizationPhase.BeforeCall));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            request,
            null!,
            synchronization,
            AddressTakenValueSynchronizationPhase.BeforeCall));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            request,
            code,
            null!,
            AddressTakenValueSynchronizationPhase.BeforeCall));
    }
}
