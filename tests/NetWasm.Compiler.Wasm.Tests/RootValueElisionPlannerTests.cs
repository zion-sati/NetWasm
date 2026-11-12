using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class RootValueElisionPlannerTests
{
    private readonly IRootValueElisionPlanner _planner =
        Assert.IsAssignableFrom<IRootValueElisionPlanner>(new RootValueElisionPlanner(new StructuredBlockPredecessorReader()));
    private static readonly RootSource Source = new(RootSourceKind.Argument, 0);

    [Fact]
    public void RepeatedLiveValuesAreSkippedButDeadSlotsMustStillBeCleared()
    {
        var context = Context([new(0, [Source], []), new(1, [Source], []),
            new(2, [], []), new(3, [Source], [])]);
        var plan = _planner.Plan(Method(Enumerable.Range(0, 4).Select(i => I(i, CilOperation.Call)).ToImmutableArray()), context);
        Assert.Empty(plan[0]);
        Assert.Contains(0, plan[1]);
        Assert.Empty(plan[2]);
        Assert.Empty(plan[3]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignmentsInvalidatePublishedValues(bool argument)
    {
        var source = new RootSource(argument ? RootSourceKind.Argument : RootSourceKind.Local, 0);
        var context = Context([new(0, [source], []), new(2, [source], [])], source);
        var plan = _planner.Plan(Method([I(0, CilOperation.Call),
            I(1, argument ? CilOperation.StoreArgument : CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.Call)]), context);
        Assert.Empty(plan[2]);
    }

    [Fact]
    public void ConstructorPhasesResetPublicationFacts()
    {
        var context = Context([new(0, [Source], []), new(1, [Source], [Source]), new(2, [Source], [])]);
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.NewObject), I(2, CilOperation.Call)]), context);
        Assert.Empty(plan[1]);
        Assert.Empty(plan[2]);
    }

    // An allocation that bumps its run inline publishes only when it calls the
    // collector, so afterwards a slot keeps only a value it already held.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalAllocationsKeepOnlyValuesPublishedBeforeThem(bool conditional)
    {
        var other = new RootSource(RootSourceKind.Argument, 1);
        var context = Context([new(0, [Source], []), new(1, [Source, other], []), new(2, [Source, other], [])]);
        context = context with
        {
            RootMap = context.RootMap with { Slots = context.RootMap.Slots.Add(other, 1) },
            ConditionalAllocationRoots = conditional,
        };
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.NewObject), I(2, CilOperation.Call)]), context);
        Assert.Equal([0], plan[1]);
        Assert.Contains(0, plan[2]);
        Assert.Equal(!conditional, plan[2].Contains(1));
    }

    // A stable variable published on every ordinary path into a block, and not assigned since,
    // still holds its slot there; an assignment on one path means it is published again.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishedValuesFollowOrdinaryEdgesAndMeetAtJoins(bool oneArmAssigns)
    {
        var context = Context([new(0, [Source], []), new(2, [Source], []), new(6, [Source], [])]);
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Call)], new StructuredConditionalExit(
                new(1, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null), new(1), new(2))),
            BlockMethods.Block(1, [I(2, CilOperation.Call)], new StructuredBranchExit(3, new(3))),
            BlockMethods.Block(2, oneArmAssigns ? [I(4, CilOperation.StoreArgument, new CilOperand.Index(0))] : [I(4, CilOperation.Nop)],
                new StructuredFallthroughExit(new(3))),
            BlockMethods.Block(3, [I(6, CilOperation.Call)], new StructuredTerminalExit(I(7, CilOperation.Return))));

        var plan = _planner.Plan(method, context);

        Assert.Empty(plan[0]);
        Assert.Equal([0], plan[2]);
        Assert.Equal(!oneArmAssigns, plan[6].Contains(0));
    }

    [Fact]
    public void ADeferredRootFrameKeepsPublishedValuesWithinBlocks()
    {
        var context = Context([new(0, [Source], []), new(2, [Source], [])]) with
        {
            TerminalBranchValueFramePlacement = new(new TerminalBranchValueFramePlan(
                new(new(0), StructuredBlockRole.Owner), StructuredSequence.Empty, StructuredSequence.Empty, true, [])
            {
                DefersRootFrame = true,
            }, new(0, [], [], [], [])),
        };
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Call)], new StructuredFallthroughExit(new(1))),
            BlockMethods.Block(1, [I(2, CilOperation.Call)], new StructuredTerminalExit(I(3, CilOperation.Return))));

        Assert.Empty(_planner.Plan(method, context)[2]);
    }

    [Fact]
    public void FactsDoNotCrossBlockEntries()
    {
        var context = Context([new(0, [Source], []), new(1, [Source], [])]);
        var method = Method([I(0, CilOperation.Call)]);
        var second = new StructuredBlockDefinition(new(1), 1, [I(1, CilOperation.Call)], [],
            new StructuredFallthroughExit(null))
        { EndOffset = 2 };
        var plan = _planner.Plan(method with { Blocks = method.Blocks.Add(second.Id, second) }, context);
        Assert.Empty(plan[1]);
    }

    [Theory]
    [InlineData(RootSourceKind.EvaluationStack, -1)]
    [InlineData(RootSourceKind.ManagedAddressLocal, -1)]
    [InlineData(RootSourceKind.Argument, 0)]
    public void UnstableSourcesAreNeverSkipped(RootSourceKind kind, int byteOffset)
    {
        var source = new RootSource(kind, 0, byteOffset);
        var context = Context([new(0, [source], []), new(1, [source], [])], source);
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.Call)]), context);
        Assert.Empty(plan[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddressableVariablesMustBeRepublishedAfterCalls(bool argument)
    {
        var source = new RootSource(argument ? RootSourceKind.Argument : RootSourceKind.Local, 0);
        var context = Context([new(0, [source], []), new(1, [source], [])], source);
        context = context with
        {
            ValueLayout = argument
            ? context.ValueLayout with { ArgumentOffsets = ImmutableDictionary<int, int>.Empty.Add(0, 0) }
            : context.ValueLayout with { SpilledScalarLocals = [0] }
        };
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.Call)]), context);
        Assert.Empty(plan[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedVariablesMustBeRepublishedAfterCalls(bool argument)
    {
        var source = new RootSource(argument ? RootSourceKind.Argument : RootSourceKind.Local, 0);
        var context = Context([new(0, [source], []), new(1, [source], [])], source);
        var slot = new CapturedSlot(argument, 0);
        context = context with
        {
            FilterEnvironment = context.FilterEnvironment with
            {
                Captures = ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(slot,
                new(slot, CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), 0, [])),
            }
        };
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.Call)]), context);
        Assert.Empty(plan[1]);
    }

    [Fact]
    public void ReusingPhysicalSlotsInvalidatesPreviousValues()
    {
        var other = new RootSource(RootSourceKind.Local, 0);
        var context = Context([new(0, [Source], []), new(1, [other], []), new(2, [Source], [])]);
        context = context with { RootMap = context.RootMap with { Slots = context.RootMap.Slots.Add(other, 0) } };
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.Call), I(2, CilOperation.Call)]), context);
        Assert.Empty(plan[1]);
        Assert.Empty(plan[2]);
    }

    [Fact]
    public void ClonedOffsetsIntersectProofsAcrossOccurrences()
    {
        var context = Context([new(0, [Source], []), new(1, [Source], [])]);
        var method = Method([I(0, CilOperation.Call), I(1, CilOperation.Call)]);
        var second = new StructuredBlockDefinition(new(1), 1, [I(1, CilOperation.Call)], [],
            new StructuredFallthroughExit(null))
        { EndOffset = 2 };
        var plan = _planner.Plan(method with { Blocks = method.Blocks.Add(second.Id, second) }, context);
        Assert.Empty(plan[1]);
    }

    [Fact]
    public void TerminalSafepointsUseTheSamePublicationProof()
    {
        var context = Context([new(0, [Source], []), new(2, [Source], [])]);
        var method = Method([I(0, CilOperation.Call), I(1, CilOperation.LoadArgument, new CilOperand.Index(0))]);
        var block = method.Blocks[new(0)] with { Exit = new StructuredTerminalExit(I(2, CilOperation.Throw)) };
        var plan = _planner.Plan(method with { Blocks = method.Blocks.SetItem(block.Id, block) }, context);
        Assert.Contains(0, plan[2]);
    }

    [Fact]
    public void BypassedAggregateInstructionsDiscardPublicationKnowledge()
    {
        var context = Context([new(0, [Source], []), new(1, [Source], []), new(2, [Source], [])]);
        var access = new ManagedAggregateSsaAccess(new(1, new(0),
            new(new(ManagedAggregateStorageKind.Local, 0), 0, CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            4, CliValueKind.I4, false), [], []);
        context = context with
        {
            ManagedAggregateValues = context.ManagedAggregateValues with
            {
                Plan = context.ManagedAggregateValues.Plan with
                { Accesses = ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty.Add(1, access) },
            }
        };
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.LoadObject), I(2, CilOperation.Call)]), context);
        Assert.False(plan.ContainsKey(1));
        Assert.Empty(plan[2]);
    }

    private static MethodEmissionContext Context(IEnumerable<SafepointRootMap> points, RootSource? source = null) =>
        CreateMethodEmissionContext() with
        {
            RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(source ?? Source, 0),
                points.ToImmutableDictionary(point => point.IlOffset)),
        };

    private static StructuredMethod Method(ImmutableArray<CilInstruction> instructions)
    {
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        var block = new StructuredBlockDefinition(new(0), 0, instructions, [],
            new StructuredFallthroughExit(null))
        { EndOffset = instructions.Length };
        return new(new(definition, null, 1, [], [], instructions), new(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(block.Id, block),
            StructuredSequence.Empty, [], [], []);
    }
}
