// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedAggregateValueReloadEmitterTests
{
    [Fact]
    public void RejectsMissingDependenciesAndInputsBeforeEmission()
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new Addresses();
        Assert.Throws<ArgumentNullException>(() => new ManagedAggregateValueReloadEmitter(null!, addresses));
        Assert.Throws<ArgumentNullException>(() => new ManagedAggregateValueReloadEmitter(layouts, null!));
        var emitter = new ManagedAggregateValueReloadEmitter(layouts, addresses);
        var writer = new RecordingInstructionWriter();
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, writer));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(CreateInstructionRequest(CilOperation.Nop), null!));
        Assert.Empty(writer.ToInstructions());
        Assert.Empty(addresses.Offsets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadRequestRequiresLaneDefinitionsButNonReloadMaterializationIsIgnored(bool reload)
    {
        var storage = new ManagedAggregateStorageId(ManagedAggregateStorageKind.Local, 0);
        var plan = ManagedAggregateSsaPlan.Empty with
        {
            Materializations = [new(new(0, storage, ReloadAfter: reload), [])],
        };
        var context = CreateMethodEmissionContext();
        context = context with
        {
            ValueLayout = context.ValueLayout with { LocalOffsets = ImmutableDictionary<int, int>.Empty.Add(0, 12) },
            ManagedAggregateValues = new(plan, []),
        };
        var request = CreateInstructionRequest(CilOperation.Call, context: context);
        var addresses = new Addresses();
        var writer = new RecordingInstructionWriter();
        var emitter = new ManagedAggregateValueReloadEmitter(new RecordingLayoutProvider(), addresses);

        if (reload) Assert.Throws<InvalidOperationException>(() => emitter.Emit(request, writer));
        else emitter.Emit(request, writer);

        Assert.Empty(writer.ToInstructions());
        Assert.Empty(addresses.Offsets);
        Assert.Empty(request.Stack);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void ReloadsBothLanesFromOwnedBacking(WasmTarget target, bool argument)
    {
        var storage = new ManagedAggregateStorageId(argument
            ? ManagedAggregateStorageKind.Argument : ManagedAggregateStorageKind.Local, 0);
        var low = new ManagedAggregateSsaLane(storage, 0);
        var high = new ManagedAggregateSsaLane(storage, 4);
        var plan = ManagedAggregateSsaPlan.Empty with
        {
            Storages = ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty
                .Add(storage, new(storage, CliTypeIdentity.FromStackKind(CliValueKind.I8), 8, true)),
            Materializations = [new(new(10, storage, ReloadAfter: true), [])
            {
                Reloads = [new(low, new(2)), new(high, new(3))],
            }],
        };
        var offsets = ImmutableDictionary<int, int>.Empty.Add(0, 12);
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new(20, argument ? [] : offsets, argument ? offsets : [], [], []),
            ManagedAggregateValues = new(plan,
                ImmutableDictionary<ManagedAggregateSsaLane, int>.Empty.Add(low, 30).Add(high, 31)),
        };
        var program = new FakeProgram();
        var header = new StructuredMethodHeader(program.GetMethod(EntryKey), null, 1, [], [], []);
        var request = new InstructionEmissionRequest(header, I(10, CilOperation.Call), [],
            context, CreateInstructionModuleTarget(program));
        var addresses = new Addresses();
        var emitter = AsEmitter(new ManagedAggregateValueReloadEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)), addresses));
        var code = new RecordingInstructionWriter();
        emitter.Emit(request, code);

        Assert.Equal([12, 16], addresses.Offsets);
        var instructions = code.ToInstructions();
        Assert.Equal(4, instructions.Length);
        Assert.Equal(WasmOpcodes.I32Load, instructions[0].Opcode);
        Assert.Equal(WasmOpcodes.LocalSet, instructions[1].Opcode);
        Assert.Equal(30u, instructions[1].Operand.UnsignedValue);
        Assert.Equal(WasmOpcodes.I32Load, instructions[2].Opcode);
        Assert.Equal(31u, instructions[3].Operand.UnsignedValue);
        var untouched = new RecordingInstructionWriter();
        emitter.Emit(request with { Instruction = I(11, CilOperation.Nop) }, untouched);
        Assert.Empty(untouched.ToInstructions());
        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            request with { Context = context with { ValueLayout = new(0, [], [], [], []) } },
            new RecordingInstructionWriter()));
    }

    private static IManagedAggregateValueReloadEmitter AsEmitter(
        IManagedAggregateValueReloadEmitter emitter) => emitter;

    private sealed class Addresses : IValueFrameAddressEmitter
    {
        public List<int> Offsets { get; } = [];
        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int offset) =>
            Offsets.Add(offset);
        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, FilterCapture capture) =>
            throw new InvalidOperationException();
    }
}
