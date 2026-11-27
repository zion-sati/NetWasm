// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// A state machine's loop, while (true) switch (state), left only from its switch: case 7 and the default return, the
// other cases continue. The switch-loop factory makes it unconditional with the whole chain in its body; any other
// loop, or one it cannot make, is the inner factory's.
public sealed class SwitchLoopRegionFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(4)]
    public void ALoopLeftOnlyFromItsSwitchIsUnconditional(int? join)
    {
        var machine = Machine();
        var partitions = new RecordingPartitioner(overlaps: false);
        var loops = new RecordingLoops();

        var created = Factory(loops, new FixedJoin(join), partitions, eligible: true)
            .Create(machine.State, machine.State.Graph, machine.Component, machine.Domain, [machine.Domain]);

        var region = Assert.IsType<LoopRegion>(created.Region);
        Assert.Empty(created.DispatcherOwnedBlocks);
        Assert.Equal(0, loops.Calls);
        Assert.Null(region.Condition);
        Assert.Equal((machine.Header, machine.Header), (region.Header, region.Continue));
        // The returning case's branch and the default's pop leave the strongly connected blocks; the body holds them,
        // and the loop is left at the cases' targets.
        Assert.Contains(machine.Block("s7"), partitions.Body);
        var exits = new[] { machine.Block("t7"), machine.Block("final") };
        Assert.Equal(join ?? exits.Min(), region.Exit);
        Assert.Equal(region.Exit, region.ConditionExit);
        Assert.Equal(exits.Order(), partitions.Exits.Order());
        Assert.Equal(partitions.Body, region.BodyComponent);
    }

    // Fewer than 16 cases, an exit from a case's code rather than the chain, a protected region in the loop, exits whose
    // code overlaps, a region the eligibility rules reject, and an entry loop entered elsewhere are the inner factory's.
    [Theory]
    [InlineData("fifteen")]
    [InlineData("exit")]
    [InlineData("protected")]
    [InlineData("overlaps")]
    [InlineData("ineligible")]
    [InlineData("entered")]
    public void AnyOtherLoopIsTheInnerFactorys(string shape)
    {
        var machine = Machine(cases: shape == "fifteen" ? 15 : 20, exit: shape == "exit", protectedCase: shape == "protected");
        var domain = shape == "entered" ? new ControlFlowDomain(machine.Header, machine.State.Graph.ReachableBlocks) : machine.Domain;
        var loops = new RecordingLoops();

        var created = Factory(loops, new FixedJoin(null), new RecordingPartitioner(overlaps: shape == "overlaps"),
                eligible: shape != "ineligible")
            .Create(machine.State, machine.State.Graph, machine.Component, domain, [domain]);

        Assert.Equal(1, loops.Calls);
        Assert.Same(loops.Result, created);
    }

    [Fact]
    public void ConstructorRejectsNullCollaborators()
    {
        var loops = new RecordingLoops();
        var chains = new SwitchChainReader();
        var tables = new SwitchTablePolicy(16, 16384);
        var joins = new FixedJoin(null);
        var partitions = new RecordingPartitioner(false);
        var eligibility = new FixedEligibility(true);
        var entryTargets = new LoopEntryTargetFinder();

        Assert.Equal("loops", Assert.Throws<ArgumentNullException>(() =>
            new SwitchLoopRegionFactory(null!, chains, tables, joins, partitions, eligibility, entryTargets)).ParamName);
        Assert.Equal("chains", Assert.Throws<ArgumentNullException>(() =>
            new SwitchLoopRegionFactory(loops, null!, tables, joins, partitions, eligibility, entryTargets)).ParamName);
        Assert.Equal("tables", Assert.Throws<ArgumentNullException>(() =>
            new SwitchLoopRegionFactory(loops, chains, null!, joins, partitions, eligibility, entryTargets)).ParamName);
        Assert.Equal("joins", Assert.Throws<ArgumentNullException>(() =>
            new SwitchLoopRegionFactory(loops, chains, tables, null!, partitions, eligibility, entryTargets)).ParamName);
        Assert.Equal("exitPartitions", Assert.Throws<ArgumentNullException>(() =>
            new SwitchLoopRegionFactory(loops, chains, tables, joins, null!, eligibility, entryTargets)).ParamName);
        Assert.Equal("eligibility", Assert.Throws<ArgumentNullException>(() =>
            new SwitchLoopRegionFactory(loops, chains, tables, joins, partitions, null!, entryTargets)).ParamName);
        Assert.Equal("entryTargets", Assert.Throws<ArgumentNullException>(() =>
            new SwitchLoopRegionFactory(loops, chains, tables, joins, partitions, eligibility, null!)).ParamName);
    }

    private static SwitchLoopRegionFactory Factory(RecordingLoops loops, FixedJoin joins, RecordingPartitioner partitions,
        bool eligible) =>
        new(loops, new SwitchChainReader(), new SwitchTablePolicy(16, 16384), joins, partitions, new FixedEligibility(eligible),
            new LoopEntryTargetFinder());

    private sealed record StateMachine(ControlFlowStructuringState State, ImmutableHashSet<int> Component, int Header,
        ControlFlowDomain Domain, Il Il)
    {
        public int Block(string label) => State.Graph.GetBlockAtOffset(Il.Offset(label)).Index;
    }

    // ldc 0; stloc 0; head: ldloc 0; switch; return -1; each case assigns the next state and branches to the head, but
    // case 7 returns; with `exit`, case 3 also returns when its argument is set; with `protectedCase`, case 3's code is
    // a protected region left to the head.
    private static StateMachine Machine(int cases = 20, bool exit = false, bool protectedCase = false)
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadInt32, 0);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Label("head");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        for (var index = 0; index < cases; index++)
        {
            il.Label($"c{index}");
            il.Emit(CilOperation.Duplicate);
            il.Emit(CilOperation.LoadInt32, index);
            il.Emit(CilOperation.BranchIfNotEqual, index == cases - 1 ? "final" : $"c{index + 1}");
            il.Label($"s{index}");
            il.Emit(CilOperation.Pop);
            il.Emit(CilOperation.Branch, $"t{index}");
        }
        il.Label("final");
        il.Emit(CilOperation.Pop);
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.Return);
        var regions = new List<CilExceptionRegion>();
        for (var index = 0; index < cases; index++)
        {
            il.Label($"t{index}");
            if (index == 7)
            {
                il.Emit(CilOperation.LoadInt32, 5);
                il.Emit(CilOperation.Return);
                continue;
            }
            if (index == 3 && exit)
            {
                il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
                il.Emit(CilOperation.BranchIfTrue, "out");
            }
            if (index == 3 && protectedCase)
            {
                // A protected region is entered by falling into it.
                il.Emit(CilOperation.Nop);
                il.Label("try");
                il.Emit(CilOperation.LoadInt32, 4);
                il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
                il.Emit(CilOperation.Leave, "head");
                il.Label("handler");
                il.Emit(CilOperation.EndFinally);
                continue;
            }
            il.Emit(CilOperation.LoadInt32, (index * 7 + 3) % cases);
            il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
            il.Emit(CilOperation.Branch, "head");
        }
        il.Label("out");
        il.Emit(CilOperation.LoadInt32, 9);
        il.Emit(CilOperation.Return);
        if (protectedCase)
        {
            regions.Add(new CilExceptionRegion(CilExceptionRegionKind.Finally, il.Offset("try"),
                il.Offset("handler") - il.Offset("try"), il.Offset("handler"), 1, null, null));
        }
        var validated = Validate(il.Body([.. regions]));
        var graph = validated.Graph;
        var header = graph.GetBlockAtOffset(il.Offset("head")).Index;
        return new StateMachine(new ControlFlowStructuringState(validated), Cycle(graph, header), header,
            new ControlFlowDomain(graph.Entry.Index, graph.ReachableBlocks), il);
    }

    // The blocks the header reaches that reach it back.
    private static ImmutableHashSet<int> Cycle(ControlFlowGraph graph, int header)
    {
        HashSet<int> Walk(Func<int, ImmutableArray<int>> next)
        {
            var seen = new HashSet<int> { header };
            var pending = new Stack<int>([header]);
            while (pending.TryPop(out var block))
            {
                foreach (var other in next(block).Where(seen.Add))
                {
                    pending.Push(other);
                }
            }
            return seen;
        }
        var forward = Walk(block => graph.Successors[block]);
        forward.IntersectWith(Walk(block => graph.Predecessors[block]));
        return [.. forward];
    }

    private sealed class RecordingLoops : ILoopRegionFactory
    {
        public int Calls { get; private set; }

        public LoopRegionCreation Result { get; } = new(null, [1]);

        public LoopRegionCreation Create(ControlFlowStructuringState state, ControlFlowGraph graph,
            ImmutableHashSet<int> component, ControlFlowDomain domain, ControlFlowDomains domains)
        {
            Calls++;
            return Result;
        }
    }

    private sealed class FixedJoin(int? result) : ICommonReachableBlockFinder
    {
        public int? Find(ControlFlowStructuringState state, int first, int second, int? stop, ImmutableHashSet<int> allowed) =>
            throw new InvalidOperationException("The switch loop asks for the join of all its exits at once.");

        public int? Find(ControlFlowStructuringState state, int[] starts, int? stop, ImmutableHashSet<int> allowed) => result;
    }

    // Partitions every exit into the body it is given, as overlapping or not.
    private sealed class RecordingPartitioner(bool overlaps) : ILoopExitPartitioner
    {
        public ImmutableHashSet<int> Body { get; private set; } = [];

        public ImmutableHashSet<int> Exits { get; private set; } = [];

        public LoopExitPartition Partition(ControlFlowGraph graph, ImmutableHashSet<int> component, int exit,
            int conditionExit, ImmutableHashSet<int> additionalExits)
        {
            (Body, Exits) = (component, additionalExits);
            return new LoopExitPartition([], component, overlaps);
        }
    }

    private sealed class FixedEligibility(bool eligible) : ILoopRegionEligibilityClassifier
    {
        public bool Classify(LoopRegionEligibilityRequest request) => eligible;
    }
}
