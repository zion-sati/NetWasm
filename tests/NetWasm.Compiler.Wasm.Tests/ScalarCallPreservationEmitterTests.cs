// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ScalarCallPreservationEmitterTests
{
    [Fact]
    public void RejectsMissingDependenciesInputsAndUnknownPhaseBeforeOutput()
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new Addresses();
        Assert.Throws<ArgumentNullException>(() => new ScalarCallPreservationEmitter(null!, addresses));
        Assert.Throws<ArgumentNullException>(() => new ScalarCallPreservationEmitter(layouts, null!));
        var emitter = new ScalarCallPreservationEmitter(layouts, addresses);
        var writer = new RecordingInstructionWriter();
        var request = Request(CliValueKind.F8);
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, writer, ScalarCallPreservationPhase.BeforeCall));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!, ScalarCallPreservationPhase.BeforeCall));
        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(request, writer, (ScalarCallPreservationPhase)99));
        Assert.Empty(writer.ToInstructions());
        Assert.Empty(addresses.Offsets);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void ValidatesEverySelectedLocalBeforeTransferringAny(int invalidLocal)
    {
        var request = Request(CliValueKind.F8);
        var layout = request.Context.ValueLayout with
        {
            ScalarCallLifetimes = new(ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(10, [0, invalidLocal])),
        };
        var writer = new RecordingInstructionWriter();
        var addresses = new Addresses();
        var emitter = new ScalarCallPreservationEmitter(new RecordingLayoutProvider(), addresses);

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            request with { Context = request.Context with { ValueLayout = layout } }, writer,
            ScalarCallPreservationPhase.BeforeCall));

        Assert.Empty(writer.ToInstructions());
        Assert.Empty(addresses.Offsets);
        Assert.Equal(CliValueKind.I8, Assert.Single(request.Stack));
    }

    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.ManagedReference)]
    public void RejectsUnsupportedScalarKindsBeforeTransfer(CliValueKind kind)
    {
        var request = Request(kind);
        var writer = new RecordingInstructionWriter();
        var addresses = new Addresses();
        var emitter = new ScalarCallPreservationEmitter(new RecordingLayoutProvider(), addresses);

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(request, writer, ScalarCallPreservationPhase.BeforeCall));

        Assert.Empty(writer.ToInstructions());
        Assert.Empty(addresses.Offsets);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F4, true)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F8, true)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F4, true)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F8, true)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F4, false)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F8, false)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F4, false)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F8, false)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8, true)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8, true)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8, false)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8, false)]
    public void TransfersExactScalarRepresentationAtSelectedBoundary(
        WasmTarget target, CliValueKind kind, bool beforeCall)
    {
        var phase = beforeCall ? ScalarCallPreservationPhase.BeforeCall : ScalarCallPreservationPhase.AfterNormalReturn;
        var request = Request(kind);
        var addresses = new Addresses();
        var emitter = AsEmitter(new ScalarCallPreservationEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)), addresses));
        var code = new RecordingInstructionWriter();

        emitter.Emit(request, code, phase);

        var instructions = code.ToInstructions();
        Assert.Equal([8], addresses.Offsets);
        Assert.Equal(2, instructions.Length);
        if (phase == ScalarCallPreservationPhase.BeforeCall)
        {
            Assert.Equal(WasmOpcodes.LocalGet, instructions[0].Opcode);
            Assert.Equal(30u, instructions[0].Operand.UnsignedValue);
            Assert.Equal(kind switch
            {
                CliValueKind.F4 => WasmOpcodes.F32Store,
                CliValueKind.F8 => WasmOpcodes.F64Store,
                _ => WasmOpcodes.I64Store
            },
                instructions[1].Opcode);
        }
        else
        {
            Assert.Equal(kind switch
            {
                CliValueKind.F4 => WasmOpcodes.F32Load,
                CliValueKind.F8 => WasmOpcodes.F64Load,
                _ => WasmOpcodes.I64Load
            },
                instructions[0].Opcode);
            Assert.Equal(WasmOpcodes.LocalSet, instructions[1].Opcode);
            Assert.Equal(30u, instructions[1].Operand.UnsignedValue);
        }
        Assert.Equal([CliValueKind.I8], request.Stack);
    }

    [Fact]
    public void UnselectedInstructionEmitsNothing()
    {
        var request = Request(CliValueKind.F8);
        var addresses = new Addresses();
        var code = new RecordingInstructionWriter();
        AsEmitter(new ScalarCallPreservationEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(WasmTarget.Wasm32)), addresses))
            .Emit(request with { Instruction = I(11, CilOperation.Call) }, code,
                ScalarCallPreservationPhase.BeforeCall);

        Assert.Empty(code.ToInstructions());
        Assert.Empty(addresses.Offsets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidSelectedStorageFailsBeforeAnyOutput(int variant)
    {
        var request = Request(CliValueKind.F8);
        var layout = request.Context.ValueLayout;
        layout = variant switch
        {
            0 => layout with { ScalarCallOffsets = [] },
            1 => layout with { Size = 12 },
            2 => layout with { SpilledScalarLocals = [0] },
            _ => layout with { ScalarCallOffsets = ImmutableDictionary<int, int>.Empty.Add(0, -1) },
        };
        var addresses = new Addresses();
        var code = new RecordingInstructionWriter();
        var emitter = AsEmitter(new ScalarCallPreservationEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(WasmTarget.Wasm32)), addresses));

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            request with { Context = request.Context with { ValueLayout = layout } }, code,
            ScalarCallPreservationPhase.BeforeCall));
        Assert.Empty(code.ToInstructions());
        Assert.Empty(addresses.Offsets);
    }

    private static InstructionEmissionRequest Request(CliValueKind kind)
    {
        var type = CliTypeIdentity.FromStackKind(kind);
        var program = new FakeProgram();
        var context = CreateMethodEmissionContext() with
        {
            LocalBase = 30,
            ValueLayout = new(16, [], [], [], [])
            {
                ScalarCallLifetimes = new(ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(10, [0])),
                ScalarCallOffsets = ImmutableDictionary<int, int>.Empty.Add(0, 8),
            },
        };
        var header = new StructuredMethodHeader(program.GetMethod(EntryKey), null, 1, [kind], [type], []);
        return new(header, I(10, CilOperation.Call), [CliValueKind.I8], context,
            CreateInstructionModuleTarget(program));
    }

    private static IScalarCallPreservationEmitter AsEmitter(IScalarCallPreservationEmitter emitter) => emitter;

    private sealed class Addresses : IValueFrameAddressEmitter
    {
        public List<int> Offsets { get; } = [];
        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int offset) => Offsets.Add(offset);
        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, FilterCapture capture) =>
            throw new InvalidOperationException();
    }
}
