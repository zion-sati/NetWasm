// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// A method enters its root frame lazily when some path from entry to a return has no safepoint with a live root; a
// protected block enters it before it begins when a handler, or the code a handler flows into, reaches one.
public sealed class LazyRootFramePlannerTests
{
    private static readonly RootSource Root = new(RootSourceKind.Local, 0);
    private readonly LazyRootFramePlanner _planner = new(new RootClearElisionPlanner(new StructuredBlockPredecessorReader()),
        new FallbackPublicationClassifier());

    [Fact]
    public void RejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new LazyRootFramePlanner(null!, new FallbackPublicationClassifier()));
        Assert.Throws<ArgumentNullException>(() => new LazyRootFramePlanner(
            new RootClearElisionPlanner(new StructuredBlockPredecessorReader()), null!));
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(null!, CreateMethodEmissionContext()));
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(Branching(), null!));
    }

    // Entry (offset 0) branches to a block that publishes at offset 1 and returns, or to one that returns at once.
    [Fact]
    public void APathThatPublishesNothingMakesTheFrameLazy()
    {
        var plan = _planner.Plan(Branching(), Context(1));
        Assert.NotNull(plan);
        Assert.Empty(plan.EagerGroups);
    }

    [Fact]
    public void EveryPathPublishingKeepsTheFrameOnEntry() => Assert.Null(_planner.Plan(Branching(), Context(0)));

    // A point that collects only by throwing to a handler leaves a deferrable root unpublished in a frame that starts
    // zeroed, so it publishes nothing and the frame stays lazy though every path passes it.
    [Fact]
    public void APointThatLeavesItsRootsUnpublishedKeepsTheFrameLazy()
    {
        var context = Context(0);
        context = context with
        {
            RootMap = context.RootMap with
            {
                Safepoints = context.RootMap.Safepoints.SetItem(0, new(0, [Root], []) { Deferrable = [Root] }),
            },
        };
        var plan = _planner.Plan(Branching(), context);
        Assert.NotNull(plan);
        Assert.Empty(plan.Entered);
    }

    // A throw is a block's exit, not one of its instructions; a safepoint there publishes in that block.
    [Fact]
    public void ASafepointAtABlocksExitPublishesInThatBlock()
    {
        var method = Branching(new StructuredTerminalExit(I(3, CilOperation.Throw)));
        Assert.NotNull(_planner.Plan(method, Context(3)));
        Assert.Null(_planner.Plan(method with
        {
            Blocks = method.Blocks.SetItem(new(2), Block(2, 2, [I(2, CilOperation.Nop)],
            new StructuredTerminalExit(I(4, CilOperation.Throw))))
        }, Context(4)));
    }

    [Fact]
    public void WhatReadsTheFrameOtherwiseKeepsItOnEntry()
    {
        var method = Branching();
        Assert.Null(_planner.Plan(method, Context(1) with { RootMap = new(EntryKey, [], []) }));
        Assert.Null(_planner.Plan(method, Context(1) with { FilterEnvironment = new FilterEnvironmentLayout(16, 1, 0, []) }));
        Assert.Null(_planner.Plan(method, Context(1) with { IsFilterFunclet = true }));
        Assert.Null(_planner.Plan(method, Context(9)));
        Assert.Null(_planner.Plan(method with { Blocks = [] }, Context(1)));
    }

    // Group 0's handler (block 3) publishes; group 1's (block 4) returns at once.
    [Fact]
    public void ABlockWhoseHandlerPublishesEntersTheFrameFirst()
    {
        var method = Branching();
        var blocks = method.Blocks
            .Add(new(3), Block(3, 5, [I(5, CilOperation.Call)], new StructuredTerminalExit(I(6, CilOperation.Return))))
            .Add(new(4), Block(4, 7, [I(7, CilOperation.Nop)], new StructuredTerminalExit(I(8, CilOperation.Return))));
        method = method with
        {
            Blocks = blocks,
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty
                .Add(new(0), Group(0, new(3)))
                .Add(new(1), Group(1, new(4))),
        };

        var plan = _planner.Plan(method, Context(5));

        Assert.NotNull(plan);
        Assert.Equal([new StructuredExceptionGroupId(0)], plan.EagerGroups);
    }

    // Block 0 -> 1 (publishes at 1, then 2) -> 3 (4), or 0 -> 2 (3): after the publication, and in the block only it
    // reaches, the frame is entered on every path; at entry, at the publication and on the other path it is not.
    [Fact]
    public void TheFrameIsEnteredAfterAPublicationOnEveryPathFromIt()
    {
        var condition = new StructuredCondition(0, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null);
        var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
            .Add(new(0), Block(0, 0, [I(0, CilOperation.LoadInt32)], new StructuredConditionalExit(condition, new(1), new(2))))
            .Add(new(1), Block(1, 1, [I(1, CilOperation.Call), I(2, CilOperation.Nop)], new StructuredFallthroughExit(new(3))))
            .Add(new(2), Block(2, 3, [I(3, CilOperation.Nop)], new StructuredTerminalExit(I(10, CilOperation.Return))))
            .Add(new(3), Block(3, 4, [I(4, CilOperation.Nop)], new StructuredTerminalExit(I(11, CilOperation.Return))));
        var method = Branching() with { Blocks = blocks };

        var plan = _planner.Plan(method, Context(1));

        Assert.NotNull(plan);
        Assert.Equal([2, 4, 11], plan.Entered.Order());
    }

    // A safepoint that publishes only on an allocation's fallback enters the frame itself there, so one on every path
    // leaves the frame lazy and entered on no path.
    [Fact]
    public void AFallbackPublicationOnEveryPathKeepsTheFrameLazy()
    {
        var planner = new LazyRootFramePlanner(new RootClearElisionPlanner(new StructuredBlockPredecessorReader()),
            new Fallbacks([0]));

        var plan = planner.Plan(Branching(), Context(0));

        Assert.NotNull(plan);
        Assert.Empty(plan.Entered);
        Assert.Null(_planner.Plan(Branching(), Context(0)));
    }

    // A method with a protected block elsewhere (block 2, handler 3): block 1 allocates inline at 1 and calls at 2. The
    // allocation publishes only on its fallback, so the fast path reaches the call with the frame not entered, and the
    // call must test and enter it: offset 2 is not entered, the return after the call is.
    [Fact]
    public void AnInlineAllocationOutsideTheProtectedBlocksDoesNotEnterTheFrameOnEveryPath()
    {
        var condition = new StructuredCondition(0, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null);
        var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
            .Add(new(0), Block(0, 0, [I(0, CilOperation.LoadInt32)], new StructuredConditionalExit(condition, new(1), new(2))))
            .Add(new(1), Block(1, 1, [I(1, CilOperation.NewObject), I(2, CilOperation.Call)],
                new StructuredTerminalExit(I(10, CilOperation.Return))))
            .Add(new(2), Block(2, 3, [I(3, CilOperation.Nop)], new StructuredTerminalExit(I(11, CilOperation.Return))))
            .Add(new(3), Block(3, 5, [I(5, CilOperation.Nop)], new StructuredTerminalExit(I(6, CilOperation.Return))));
        var method = Branching() with
        {
            Blocks = blocks,
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty
                .Add(new(0), Group(0, new(3)) with { ProtectedBlocks = [new(2)] }),
        };

        var plan = _planner.Plan(method, Context(conditionalAllocations: true, 1, 2));

        Assert.NotNull(plan);
        Assert.DoesNotContain(2, plan.Entered);
        Assert.Contains(10, plan.Entered);
        Assert.Empty(plan.EagerGroups);
    }

    // Group 0's handler (block 3) allocates inline at 5: its fallback may enter the frame inside the handler's code, so
    // the protected block enters it before it begins, though no path through the allocation has to enter it.
    [Fact]
    public void AHandlerThatMayEnterTheFrameOnAnAllocationsFallbackMakesItsBlockEager()
    {
        var method = Branching();
        method = method with
        {
            Blocks = method.Blocks.Add(new(3),
                Block(3, 5, [I(5, CilOperation.NewObject)], new StructuredTerminalExit(I(6, CilOperation.Return)))),
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty
                .Add(new(0), Group(0, new(3)) with { ProtectedBlocks = [new(1)] }),
        };

        var plan = _planner.Plan(method, Context(conditionalAllocations: true, 1, 5));

        Assert.NotNull(plan);
        Assert.Equal([new StructuredExceptionGroupId(0)], plan.EagerGroups);
    }

    // Block 0 -> 1 (publishes, returns) or 2 (returns); the publishing block's instruction offsets as given.
    private static StructuredMethod Branching(StructuredBlockExit? publishingExit = null)
    {
        var condition = new StructuredCondition(0, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null);
        var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
            .Add(new(0), Block(0, 0, [I(0, CilOperation.LoadInt32)], new StructuredConditionalExit(condition, new(1), new(2))))
            .Add(new(1), Block(1, 1, [I(1, CilOperation.Call)], publishingExit ?? new StructuredTerminalExit(I(10, CilOperation.Return))))
            .Add(new(2), Block(2, 2, [I(2, CilOperation.Nop)], new StructuredTerminalExit(I(11, CilOperation.Return))));
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 1);
        return new(new(definition, null, 1, [], [], []), new(0), blocks, StructuredSequence.Empty, [], [], []);
    }

    private static StructuredBlockDefinition Block(int id, int start, ImmutableArray<CilInstruction> instructions,
        StructuredBlockExit exit) => new(new(id), start, instructions, [], exit) { EndOffset = start + instructions.Length };

    private static StructuredExceptionGroup Group(int id, StructuredBlockId handler) =>
        new(new(id), null, 0, 3, [], [
            new StructuredExceptionClause(CilExceptionRegionKind.Catch, 5, 1, TypeKey, null, StructuredSequence.Empty, null)
            {
                HandlerBlock = handler,
            },
        ], [], null, null)
        { ProtectedBlocks = [new(0), new(1), new(2)] };

    private sealed class Fallbacks(ImmutableHashSet<int> offsets) : IFallbackPublicationClassifier
    {
        public ImmutableHashSet<int> Classify(StructuredMethod method, MethodEmissionContext context) => offsets;
    }

    // A frame of one slot and a live root at each safepoint offset given; an allocation there bumps inline when
    // conditionalAllocations holds.
    private static MethodEmissionContext Context(bool conditionalAllocations, params int[] publishedAt) =>
        CreateMethodEmissionContext() with
        {
            ConditionalAllocationRoots = conditionalAllocations,
            RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(Root, 0),
                publishedAt.ToImmutableDictionary(offset => offset, offset => new SafepointRootMap(offset, [Root], []))),
        };

    // A frame of one slot and a live root at the safepoint offset given.
    private static MethodEmissionContext Context(int publishedAt) => CreateMethodEmissionContext() with
    {
        RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(Root, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(publishedAt, new(publishedAt, [Root], []))),
    };
}
