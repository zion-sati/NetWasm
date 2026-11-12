using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class RootClearElisionPlannerTests
{
    private readonly IRootClearElisionPlanner _planner =
        Assert.IsAssignableFrom<IRootClearElisionPlanner>(new RootClearElisionPlanner(new StructuredBlockPredecessorReader()));
    private static readonly RootSource Source = new(RootSourceKind.Local, 0);

    [Fact]
    public void RepeatedDeadClearsAreSkippedUntilALivePublication()
    {
        var instructions = Enumerable.Range(0, 5).Select(i => I(i, CilOperation.Call)).ToImmutableArray();
        var context = Context(Enumerable.Range(0, 5).Select(i => new SafepointRootMap(i, i == 2 ? [Source] : [], [])));
        var plan = _planner.Plan(Method(instructions), context);
        Assert.Empty(plan[(0, false)]);
        Assert.Contains(0, plan[(1, false)]);
        Assert.Empty(plan[(2, false)]);
        Assert.Empty(plan[(3, false)]);
        Assert.Contains(0, plan[(4, false)]);
    }

    // Storing a byref argument writes its authoritative slot.
    [Fact]
    public void AStoredByrefArgumentInvalidatesItsSlotsZeroFact()
    {
        var context = Context([new(0, [], []), new(2, [], [])]);
        context = context with
        {
            AuthoritativeRootSlots = ImmutableDictionary<RootSource, int>.Empty
                .Add(new(RootSourceKind.ManagedAddressArgument, 0), 0),
        };
        var plan = _planner.Plan(Method([I(0, CilOperation.Call),
            I(1, CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(2, CilOperation.Call)]), context);
        Assert.Empty(plan[(2, false)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthoritativeAssignmentInvalidatesTheZeroFact(bool argument)
    {
        var source = new RootSource(argument ? RootSourceKind.Argument : RootSourceKind.Local, 0);
        var context = Context([new(0, [], []), new(2, [], [])]);
        context = context with { AuthoritativeRootSlots = ImmutableDictionary<RootSource, int>.Empty.Add(source, 0) };
        var plan = _planner.Plan(Method([I(0, CilOperation.Call),
            I(1, argument ? CilOperation.StoreArgument : CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.Call)]), context);
        Assert.Empty(plan[(2, false)]);
    }

    [Fact]
    public void ConstructorPublicationPreventsNormalPhaseAndSuccessorElision()
    {
        var context = Context([new(0, [], []), new(1, [], [Source]), new(2, [], [])]);
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.NewObject),
            I(2, CilOperation.Call)]), context);
        Assert.Empty(plan[(1, false)]);
        Assert.Empty(plan[(1, true)]);
        Assert.Empty(plan[(2, false)]);
    }

    // An allocation that bumps its run inline clears dead slots only when it calls
    // the collector, so afterwards a slot is known zero only if it was before.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalAllocationsKeepOnlyZeroFactsFromBeforeThem(bool conditional)
    {
        var other = new RootSource(RootSourceKind.Local, 1);
        var context = TwoSlots(Context([new(0, [other], []), new(1, [], []), new(2, [], [])]), conditional);
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.NewObject),
            I(2, CilOperation.Call)]), context);
        Assert.Equal([0], plan[(1, false)]);
        Assert.Equal(!conditional, plan[(2, false)].Contains(1));
    }

    // The constructor call's publication follows the allocation's on both paths,
    // but may skip only the slots that were zero before an inline allocation.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorPublicationAfterAConditionalAllocationSkipsOnlyEarlierZeros(bool conditional)
    {
        var other = new RootSource(RootSourceKind.Local, 1);
        var context = TwoSlots(Context([new(0, [other], []), new(1, [], [Source]), new(2, [], [])]), conditional);
        var plan = _planner.Plan(Method([I(0, CilOperation.Call), I(1, CilOperation.NewObject),
            I(2, CilOperation.Call)]), context);
        Assert.Equal(!conditional, plan[(1, true)].Contains(1));
        Assert.Contains(1, plan[(2, false)]);
    }

    private static MethodEmissionContext TwoSlots(MethodEmissionContext context, bool conditional) =>
        context with
        {
            RootMap = context.RootMap with
            {
                Slots = context.RootMap.Slots.Add(new RootSource(RootSourceKind.Local, 1), 1),
            },
            ConditionalAllocationRoots = conditional,
        };

    [Fact]
    public void FactsDoNotCrossBlockEntries()
    {
        var context = Context([new(0, [], []), new(1, [], [])]);
        var method = Method([I(0, CilOperation.Call)]);
        var second = new StructuredBlockDefinition(new(1), 1, [I(1, CilOperation.Call)], [],
            new StructuredFallthroughExit(null))
        { EndOffset = 2 };
        var plan = _planner.Plan(method with { Blocks = method.Blocks.Add(second.Id, second) }, context);
        Assert.Empty(plan[(1, false)]);
    }

    // A slot cleared on every ordinary path into a block is still zero there (a root frame starts
    // with garbage, so only a publication proves it); one path that publishes into it is enough to
    // need the clear again.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroFactsFollowOrdinaryEdgesAndMeetAtJoins(bool oneArmPublishes)
    {
        var context = Context([new(0, [], []), new(2, [], []), new(4, oneArmPublishes ? [Source] : [], []),
            new(6, [], [])]);

        var plan = _planner.Plan(Diamond(), context);

        Assert.Empty(plan[(0, false)]);
        Assert.Equal([0], plan[(2, false)]);
        Assert.Equal(oneArmPublishes, plan[(4, false)].IsEmpty);
        Assert.Equal(!oneArmPublishes, plan[(6, false)].Contains(0));
    }

    // A loop header keeps only what also holds at the end of its body: here the body assigns the
    // authoritative local, so the header clears again.
    [Fact]
    public void LoopHeadersKeepOnlyFactsTheBackEdgeKeeps()
    {
        var context = Context([new(0, [], []), new(2, [], [])]) with
        {
            AuthoritativeRootSlots = ImmutableDictionary<RootSource, int>.Empty.Add(Source, 0),
        };
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Call)], new StructuredFallthroughExit(new(1))),
            BlockMethods.Block(1, [I(2, CilOperation.Call), I(3, CilOperation.StoreLocal, new CilOperand.Index(0))],
                new StructuredConditionalExit(new(4, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null), new(1), new(2))),
            BlockMethods.Block(2, [I(5, CilOperation.Return)], new StructuredFallthroughExit(null)));

        Assert.Empty(_planner.Plan(method, context)[(2, false)]);
    }

    // A root frame entered inside a terminal arm starts with garbage where the arm begins.
    [Fact]
    public void ADeferredRootFrameKeepsFactsWithinBlocks()
    {
        var context = Context([new(0, [], []), new(2, [], []), new(4, [], []), new(6, [], [])]) with
        {
            TerminalBranchValueFramePlacement = new(new TerminalBranchValueFramePlan(
                new(new(0), StructuredBlockRole.Owner), StructuredSequence.Empty, StructuredSequence.Empty, true, [])
            {
                DefersRootFrame = true,
            }, new(0, [], [], [], [])),
        };

        var plan = _planner.Plan(Diamond(), context);

        Assert.Empty(plan[(2, false)]);
        Assert.Empty(plan[(6, false)]);
    }

    private static StructuredMethod Diamond() => BlockMethods.Of(
        BlockMethods.Block(0, [I(0, CilOperation.Call)], new StructuredConditionalExit(
            new(1, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null), new(1), new(2))),
        BlockMethods.Block(1, [I(2, CilOperation.Call)], new StructuredBranchExit(3, new(3))),
        BlockMethods.Block(2, [I(4, CilOperation.Call)], new StructuredFallthroughExit(new(3))),
        BlockMethods.Block(3, [I(6, CilOperation.Call)], new StructuredTerminalExit(I(7, CilOperation.Return))));

    [Fact]
    public void ClonedOffsetsRequireZeroProofAtEveryOccurrence()
    {
        var context = Context([new(0, [], []), new(1, [], []), new(2, [], [])]);
        var method = Method([I(0, CilOperation.Call), I(1, CilOperation.Call), I(2, CilOperation.Call)]);
        var clone = new StructuredBlockDefinition(new(1), 1,
            [I(1, CilOperation.Call), I(2, CilOperation.Call)], [],
            new StructuredFallthroughExit(null))
        { EndOffset = 3 };

        var plan = _planner.Plan(method with { Blocks = method.Blocks.Add(clone.Id, clone) }, context);

        Assert.Empty(plan[(0, false)]);
        Assert.Empty(plan[(1, false)]);
        Assert.Equal([0], plan[(2, false)]);
    }

    [Fact]
    public void EmptyRootLayoutsNeedNoPlan()
    {
        Assert.Empty(_planner.Plan(Method([]), CreateMethodEmissionContext()));
    }

    [Fact]
    public void BypassedAggregateInstructionsDoNotInventPublicationEffects()
    {
        var context = Context([new(0, [], []), new(1, [Source], []), new(2, [], [])]);
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
        Assert.False(plan.ContainsKey((1, false)));
        Assert.Contains(0, plan[(2, false)]);
    }

    [Fact]
    public void TerminalSafepointsRetainKnownZeroProofs()
    {
        var context = Context([new(0, [], []), new(2, [], [])]);
        var method = Method([I(0, CilOperation.Call), I(1, CilOperation.LoadNull)]);
        var block = method.Blocks[new(0)] with { Exit = new StructuredTerminalExit(I(2, CilOperation.Throw)) };
        var plan = _planner.Plan(method with { Blocks = method.Blocks.SetItem(block.Id, block) }, context);
        Assert.Contains(0, plan[(2, false)]);
    }

    private static MethodEmissionContext Context(IEnumerable<SafepointRootMap> points) =>
        CreateMethodEmissionContext() with
        {
            RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(Source, 0),
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
