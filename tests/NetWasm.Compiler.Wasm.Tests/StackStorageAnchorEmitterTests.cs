// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StackStorageAnchorEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void AppliesCaptureCopyAndClearToTheirPhysicalDestinations(WasmTarget target, bool shared)
    {
        var assignments = ImmutableArray.Create<StackStorageAnchorAssignment>(
            new StackStorageAnchorAssignment.Capture(0, 0),
            new StackStorageAnchorAssignment.Copy(1, 0),
            new StackStorageAnchorAssignment.Clear(1));
        var context = Context(assignments, shared);
        var request = CreateInstructionRequest(CilOperation.Call, [CliValueKind.ManagedAddress], context: context);
        var addresses = new RecordingAddresses();
        var values = new RecordingValues();
        var code = new RecordingInstructionWriter();
        var emitter = CreateEmitter(target, addresses, values);
        emitter.Emit(request, code);
        Assert.Equal(0, Assert.Single(values.Anchors));
        Assert.Same(context, values.Context);
        var instructions = code.ToInstructions();
        var stackLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(context.StackLocals, 0,
            CliValueKind.ManagedAddress, WasmTargetLayout.For(target));
        Assert.Equal(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)stackLocal)), instructions[0]);
        if (shared)
        {
            Assert.Equal([24, 24], addresses.Offsets);
            Assert.Equal(2, instructions.Count(instruction => instruction.Opcode ==
                (target == WasmTarget.Wasm64 ? WasmOpcodes.I64Store : WasmOpcodes.I32Store)));
            Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.LocalSet &&
                instruction.Operand == WasmInstructionOperand.Unsigned(100));
        }
        else
        {
            Assert.Empty(addresses.Offsets);
            Assert.Equal([100u, 101u, 101u], instructions.Where(instruction =>
                instruction.Opcode == WasmOpcodes.LocalSet).Select(instruction => instruction.Operand.UnsignedValue));
        }
        Assert.Contains(instructions, instruction => instruction.Opcode ==
            (target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RejectsInvalidAssignmentsBeforeAnyWriteOrCollaboratorCall(int invalid)
    {
        StackStorageAnchorAssignment broken = invalid switch
        {
            0 => new StackStorageAnchorAssignment.Clear(2),
            1 => new StackStorageAnchorAssignment.Copy(0, 2),
            2 => new StackStorageAnchorAssignment.Capture(0, 1),
            3 => new StackStorageAnchorAssignment.Capture(0, 0),
            _ => new UnknownAssignment(0),
        };
        var context = Context([new StackStorageAnchorAssignment.Clear(1), broken], false);
        var request = CreateInstructionRequest(CilOperation.Call,
            invalid == 3 ? [CliValueKind.I4] : [CliValueKind.ManagedAddress], context: context);
        var addresses = new RecordingAddresses();
        var values = new RecordingValues();
        var code = new RecordingInstructionWriter();
        var emitter = CreateEmitter(WasmTarget.Wasm32, addresses, values);
        Assert.Throws<InvalidOperationException>(() => emitter.Emit(request, code));
        Assert.Empty(code.ToInstructions());
        Assert.Empty(values.Anchors);
        Assert.Empty(addresses.Offsets);
    }

    [Fact]
    public void MissingPhysicalStorageFailsBeforeWriting()
    {
        var context = Context([new StackStorageAnchorAssignment.Clear(0)], false) with { AnchorLocals = [] };
        var request = CreateInstructionRequest(CilOperation.Nop, context: context);
        var code = new RecordingInstructionWriter();
        var emitter = CreateEmitter(WasmTarget.Wasm32, new RecordingAddresses(), new RecordingValues());
        Assert.Throws<InvalidOperationException>(() => emitter.Emit(request, code));
        Assert.Empty(code.ToInstructions());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatedSelfCopiesDoNotEmitLoadsStoresOrCollaboratorCalls(bool shared)
    {
        var context = Context([new StackStorageAnchorAssignment.Copy(1, 1)], shared);
        var request = CreateInstructionRequest(CilOperation.Nop, context: context);
        var addresses = new RecordingAddresses();
        var values = new RecordingValues();
        var emitter = CreateEmitter(WasmTarget.Wasm32, addresses, values);
        var code = new RecordingInstructionWriter();
        emitter.Emit(request, code);
        Assert.Empty(code.ToInstructions());
        Assert.Empty(addresses.Offsets);
        Assert.Empty(values.Anchors);
        var missing = request with { Context = context with { AnchorLocals = [], FilterEnvironment = FilterEnvironmentLayout.Empty } };
        Assert.Throws<InvalidOperationException>(() => emitter.Emit(missing, code));
        Assert.Empty(code.ToInstructions());
    }

    [Fact]
    public void EmptyAssignmentsAndMissingInputMakeNoCollaboratorCalls()
    {
        var addresses = new RecordingAddresses();
        var values = new RecordingValues();
        var code = new RecordingInstructionWriter();
        var emitter = CreateEmitter(WasmTarget.Wasm32, addresses, values);
        var request = CreateInstructionRequest(CilOperation.Nop);
        emitter.Emit(request, code);
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, code));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!));
        Assert.Empty(code.ToInstructions());
        Assert.Empty(values.Anchors);
        Assert.Empty(addresses.Offsets);
    }

    private static IStackStorageAnchorEmitter CreateEmitter(WasmTarget target,
        IValueFrameAddressEmitter addresses, IStackStorageAnchorValueEmitter values) =>
        new IStackStorageAnchorEmitter[]
        { new StackStorageAnchorEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)), addresses, values) }[0];

    private static MethodEmissionContext Context(ImmutableArray<StackStorageAnchorAssignment> assignments, bool shared)
    {
        var type = CliTypeIdentity.Named(Assembly, "Tests", "Pair", true);
        return CreateMethodEmissionContext() with
        {
            RootMap = new(EntryKey, [], [])
            {
                AnchorPlan = new([new(new(StackStorageLocationKind.EvaluationStack, 0), type),
                    new(new(StackStorageLocationKind.Local, 0), type)],
                    ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty.Add(0, assignments)),
            },
            AnchorLocals = shared ? ImmutableDictionary<int, int>.Empty.Add(0, 100) :
                ImmutableDictionary<int, int>.Empty.Add(0, 100).Add(1, 101),
            FilterEnvironment = FilterEnvironmentLayout.Empty with
            {
                AnchorOffsets = shared ? ImmutableDictionary<int, int>.Empty.Add(1, 24) : [],
            },
        };
    }

    private sealed record UnknownAssignment(int Anchor) : StackStorageAnchorAssignment(Anchor);

    private sealed class RecordingValues : IStackStorageAnchorValueEmitter
    {
        public List<int> Anchors { get; } = [];
        public MethodEmissionContext? Context { get; private set; }

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int anchor)
        {
            Anchors.Add(anchor);
            Context = context;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(99)));
        }
    }

    private sealed class RecordingAddresses : IValueFrameAddressEmitter
    {
        public List<int> Offsets { get; } = [];

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int offset) => Offsets.Add(offset);

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, FilterCapture capture) =>
            throw new InvalidOperationException();
    }
}
