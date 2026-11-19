// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// A method zeroes its root frame on entry when that removes more null stores at its safepoints than the zeroing
// writes; a frame with no slots, or one entered inside a terminal arm, is never zeroed here.
public sealed class RootFrameEntryClearPlannerTests
{
    private readonly RootFrameEntryClearPlanner _planner = new(
        new RootClearElisionPlanner(new StructuredBlockPredecessorReader()), new RecordingLayoutProvider());

    [Fact]
    public void RejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(null!, CreateMethodEmissionContext()));
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(Method(1), null!));
    }

    // One safepoint where every slot is dead: an unzeroed frame clears each slot there, a zeroed one needs no store.
    // Zeroing writes one store per 16 bytes (four 4-byte slots), so it pays from two slots.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(8, true)]
    public void ZeroesWhenTheEntryWritesFewerStoresThanItRemoves(int slots, bool expected) =>
        Assert.Equal(expected, _planner.Plan(Method(1), Context(slots, [new(0, [], [])])));

    // A slot live at the only safepoint is published there either way, so zeroing removes nothing.
    [Fact]
    public void LiveSlotsGiveZeroingNothingToRemove()
    {
        var sources = Enumerable.Range(0, 4).Select(index => new RootSource(RootSourceKind.Local, index)).ToImmutableArray();
        Assert.False(_planner.Plan(Method(1), Context(4, [new(0, sources, [])])));
    }

    // A slot live at one safepoint and dead at the next is not known null there in either frame, so it is cleared
    // there either way; the other slot, never written, is all zeroing saves, which does not pay for two slots' zeroing.
    [Fact]
    public void ASlotLiveAtAnEarlierSafepointIsClearedLaterEitherWay() =>
        Assert.False(_planner.Plan(Method(2), Context(2, [new(0, [new(RootSourceKind.Local, 0)], []), new(1, [], [])])));

    // A safepoint at an aggregate access the emitter takes aside proves no slot null in either frame, so the two
    // slots that would make zeroing pay at an ordinary safepoint are cleared there either way.
    [Fact]
    public void ASafepointAtABypassedAggregateAccessKnowsNoSlotNull()
    {
        var access = new ManagedAggregateSsaAccess(new(0, new(0),
            new(new(ManagedAggregateStorageKind.Local, 0), 0, CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            4, CliValueKind.I4, false), [], []);
        var context = Context(2, [new(0, [], [])]);
        context = context with
        {
            ManagedAggregateValues = context.ManagedAggregateValues with
            {
                Plan = context.ManagedAggregateValues.Plan with
                { Accesses = ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty.Add(0, access) },
            },
        };

        Assert.False(_planner.Plan(Method(1), context));
    }

    [Fact]
    public void AFrameWithoutSlotsIsNotZeroed() =>
        Assert.False(_planner.Plan(Method(1), Context(0, [new(0, [], [])])));

    private static MethodEmissionContext Context(int slots, IEnumerable<SafepointRootMap> points) =>
        CreateMethodEmissionContext() with
        {
            RootMap = new(EntryKey,
                Enumerable.Range(0, slots).ToImmutableDictionary(index => new RootSource(RootSourceKind.Local, index), index => index),
                points.ToImmutableDictionary(point => point.IlOffset)),
        };

    private static StructuredMethod Method(int calls)
    {
        var instructions = Enumerable.Range(0, calls).Select(index => I(index, CilOperation.Call)).ToImmutableArray();
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 1);
        var block = new StructuredBlockDefinition(new(0), 0, instructions, [], new StructuredFallthroughExit(null))
        { EndOffset = instructions.Length };
        return new(new(definition, null, 1, [], [], instructions), new(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(block.Id, block),
            StructuredSequence.Empty, [], [], []);
    }
}
