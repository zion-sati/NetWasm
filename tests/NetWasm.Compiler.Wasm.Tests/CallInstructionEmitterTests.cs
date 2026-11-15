// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class CallInstructionEmitterTests
{
    [Fact]
    public void UsesTheEmitterRegisteredForTheResolvedKind()
    {
        var direct = new RecordingCallEmitter();
        var emitter = new CallInstructionEmitter(
            CreateMethodOperands(new FakeProgram()),
            new FixedCallEmissionKindResolver(CallEmissionKind.Direct),
            new CallEmissionRegistry(
                Enum.GetValues<CallEmissionKind>()
                    .Select(kind => new CallEmissionRegistration(kind, direct))),
            new AddressTakenScalarSynchronizationEmitter(
                new RecordingLayoutProvider()),
            new AddressTakenAggregateSynchronizationEmitter(
                new RecordingLayoutProvider()));
        var functionIndices = CreateFunctionIndexResolver();
        var request = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.I4],
            new CilOperand.Entity(EntryKey));

        var code = new RecordingInstructionWriter();
        emitter.Commands
            .Single(command => command.Operation == request.Instruction.Operation)
            .Emit(request, code, functionIndices);

        Assert.Equal(1, direct.Emissions);
        Assert.Same(functionIndices, direct.FunctionIndices);
    }

    [Fact]
    public void ResolvesConstrainedPrefixesOnlyFromAClosedTypeOperand()
    {
        var direct = new RecordingCallEmitter();
        var emitter = new CallInstructionEmitter(
            CreateMethodOperands(new FakeProgram()),
            new FixedCallEmissionKindResolver(CallEmissionKind.Direct),
            new CallEmissionRegistry(
                Enum.GetValues<CallEmissionKind>()
                    .Select(kind => new CallEmissionRegistration(kind, direct))),
            new AddressTakenScalarSynchronizationEmitter(
                new RecordingLayoutProvider()),
            new AddressTakenAggregateSynchronizationEmitter(
                new RecordingLayoutProvider()));
        var constrainedType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);

        Emit(emitter, CreateCallRequest(CilOperation.Nop, new CilOperand.None()));
        Assert.Null(direct.LastRequest!.ConstrainedType);

        Emit(emitter, CreateCallRequest(
            CilOperation.Constrained,
            new CilOperand.None()));
        Assert.Null(direct.LastRequest!.ConstrainedType);

        Emit(emitter, CreateCallRequest(
            CilOperation.Constrained,
            new CilOperand.TypeIdentity(constrainedType)));
        Assert.Same(constrainedType, direct.LastRequest!.ConstrainedType);
    }

    [Fact]
    public void SequencesPlannedSynchronizationAroundTheSelectedCallEmitter()
    {
        var events = new List<string>();
        var direct = new RecordingCallEmitter(() => events.Add("call"));
        var synchronization = new RecordingSynchronizationEmitter(events);
        var aggregateSynchronization =
            new RecordingAggregateSynchronizationEmitter(events);
        var emitter = new CallInstructionEmitter(
            CreateMethodOperands(new FakeProgram()),
            new FixedCallEmissionKindResolver(CallEmissionKind.Direct),
            new CallEmissionRegistry(
                Enum.GetValues<CallEmissionKind>()
                    .Select(kind => new CallEmissionRegistration(kind, direct))),
            synchronization,
            aggregateSynchronization);
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var plan = new AddressTakenScalarPromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, type),
            ImmutableDictionary<int, AddressTakenScalarCallSynchronization>.Empty.Add(
                0,
                new([0], [0])));
        var aggregateType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Aggregate",
            isValueType: true);
        var aggregatePlan = new AddressTakenAggregatePromotionPlan(
            ImmutableDictionary<int, AddressTakenAggregateLocalPlan>.Empty.Add(
                1,
                AddressTakenAggregateLocalPlan.CreateI4Storage(aggregateType, 8)),
            [],
            ImmutableDictionary<int, AddressTakenAggregateCallSynchronization>.Empty
                .Add(0, new([1], [1])));
        var request = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.I4],
            new CilOperand.Entity(EntryKey),
            CreateMethodEmissionContext() with
            {
                AddressTakenScalarPromotions = new(plan, []),
                AddressTakenAggregatePromotions = new(aggregatePlan, []),
            });

        Emit(emitter, request);

        Assert.Equal(
            [
                "ScalarBeforeCall",
                "AggregateBeforeCall",
                "call",
                "AggregateAfterCall",
                "ScalarAfterCall",
            ],
            events);
    }

    private static InstructionEmissionRequest CreateCallRequest(
        CilOperation prefixOperation,
        CilOperand prefixOperand)
    {
        var program = new FakeProgram();
        var prefix = I(0, prefixOperation, prefixOperand);
        var call = I(1, CilOperation.Call, new CilOperand.Entity(EntryKey));
        var request = new InstructionEmissionRequest(
            Header(new CilMethodBody(program.GetMethod(EntryKey), 3, [], [prefix, call])),
            call,
            [CliValueKind.I4],
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program));
        RegisterInstructionWriter(request, new RecordingInstructionWriter());
        return request;
    }

    private static void Emit(
        CallInstructionEmitter emitter,
        InstructionEmissionRequest request) => emitter.Commands
        .Single(command => command.Operation == request.Instruction.Operation)
        .Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

    private sealed class FixedCallEmissionKindResolver(CallEmissionKind kind) :
        ICallEmissionKindResolver
    {
        public CallEmissionKind Resolve(
            CallEmissionRequest request,
            IWasmInstructionWriter code) => kind;
    }

    private sealed class RecordingCallEmitter(Action? onEmit = null) : ICallEmitter
    {
        public int Emissions { get; private set; }
        public IFunctionIndexResolver? FunctionIndices { get; private set; }
        public CallEmissionRequest? LastRequest { get; private set; }

        public void Emit(
            CallEmissionRequest request,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            onEmit?.Invoke();
            Emissions++;
            FunctionIndices = functionIndices;
            LastRequest = request;
        }
    }

    private sealed class RecordingSynchronizationEmitter(List<string> events) :
        IAddressTakenScalarSynchronizationEmitter
    {
        public void Emit(
            InstructionEmissionRequest request,
            IWasmInstructionWriter code,
            AddressTakenScalarCallSynchronization synchronization,
            AddressTakenValueSynchronizationPhase phase) =>
            events.Add($"Scalar{phase}");
    }

    private sealed class RecordingAggregateSynchronizationEmitter(
        List<string> events) : IAddressTakenAggregateSynchronizationEmitter
    {
        public void Emit(
            InstructionEmissionRequest request,
            IWasmInstructionWriter code,
            int localIndex,
            AddressTakenValueSynchronizationPhase phase) =>
            events.Add($"Aggregate{phase}{localIndex}");

        public void Emit(
            InstructionEmissionRequest request,
            IWasmInstructionWriter code,
            AddressTakenAggregateCallSynchronization synchronization,
            AddressTakenValueSynchronizationPhase phase) =>
            events.Add($"Aggregate{phase}");
    }
}
