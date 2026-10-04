using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ControlFlowStructuringStateBuilderTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void BuildUsesProjectedEdgesOnlyForDiscovery(
        bool extendsComponent, bool hasExceptions, bool expectsLoop)
    {
        var body = ControlFlowTestSupport.Body(CliValueKind.Void, 0, [],
            ControlFlowTestSupport.I(0, CilOperation.Return)) with
        {
            ExceptionRegions = hasExceptions
                ? [new(CilExceptionRegionKind.Catch, 0, 1, 1, 1, ControlFlowTestSupport.TypeKey, null)]
                : [],
        };
        var original = ControlFlowStructuringStateTestFactory.Create().Graph with { };
        var graph = new ControlFlowGraph(body, original.Blocks, original.Successors,
            original.Predecessors, original.ExceptionalSuccessors, original.ExceptionalPredecessors,
            original.ReachableBlocks);
        var validated = new ValidatedControlFlowGraph(graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);
        var discovery = graph with { };
        var candidate = ImmutableHashSet.Create(0);
        var bodyComponent = extendsComponent ? candidate.Add(1) : candidate;
        var loop = new LoopRegion(0, 0, 0, 2, 2, candidate, bodyComponent, []);
        var domains = new RecordingDomains(discovery);
        var naturalLoops = new RecordingNaturalLoops(discovery);
        var components = new RecordingComponents(discovery);
        var cycles = new RecordingCycles(discovery);
        var loops = new RecordingLoopFactory(graph, loop);
        var projection = new RecordingProjection(graph, discovery);
        var builder = Assert.IsAssignableFrom<IControlFlowStructuringStateBuilder>(
            new ControlFlowStructuringStateBuilder(projection, components, naturalLoops,
                domains, loops, cycles));

        var state = builder.Build(validated);

        Assert.Same(validated, state.Validated);
        Assert.Same(graph, state.Graph);
        Assert.Equal(1, projection.CallCount);
        Assert.Equal(1, domains.CallCount);
        Assert.Equal(1, naturalLoops.CallCount);
        Assert.Equal(1, components.CallCount);
        Assert.Equal(3, cycles.CallCount);
        Assert.Equal(1, loops.CallCount);
        Assert.Equal(expectsLoop, state.LoopsByHeader.ContainsKey(0));
        if (expectsLoop)
        {
            Assert.Same(loop, state.LoopsByHeader[0]);
            Assert.False(state.DispatchersByNode.ContainsKey(0));
        }
        else
        {
            Assert.True(state.DispatchersByNode[0].SetEquals(candidate));
        }
        Assert.True(state.DispatchersByNode[2].SetEquals([2]));
        Assert.False(state.DispatchersByNode.ContainsKey(3));
    }

    [Fact]
    public void BuildRejectsNullBeforeCallingDependencies()
    {
        var builder = Assert.IsAssignableFrom<IControlFlowStructuringStateBuilder>(
            new ControlFlowStructuringStateBuilder(new IdentityExceptionContinuationGraphProjector(),
                new UnusedControlFlowComponentAnalyzer(), new UnusedControlFlowNaturalLoopAnalyzer(),
                new UnusedControlFlowDomainFinder(), new UnusedLoopRegionFactory(),
                new UnusedControlFlowCycleClassifier()));

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
    }

    [Fact]
    public void BuildPublishesEveryBlockOwnedByLoopFallbackDispatcher()
    {
        var validated = ControlFlowStructuringStateTestFactory.Create().Validated;
        var candidate = ImmutableHashSet.Create(0);
        var dispatcherOwnedBlocks = ImmutableHashSet.Create(0, 1);
        var domain = new ControlFlowDomain(0, dispatcherOwnedBlocks);
        var loops = new FixedLoopRegionFactory(new LoopRegionCreation(
            null,
            dispatcherOwnedBlocks));
        var builder = Assert.IsAssignableFrom<IControlFlowStructuringStateBuilder>(
            new ControlFlowStructuringStateBuilder(
            new IdentityExceptionContinuationGraphProjector(),
            new EmptyControlFlowComponentAnalyzer(),
            new FixedControlFlowNaturalLoopAnalyzer(candidate),
            new FixedControlFlowDomainFinder(domain),
            loops,
            new UnusedControlFlowCycleClassifier()));

        var state = builder.Build(validated);

        Assert.Empty(state.LoopsByHeader);
        Assert.Equal(2, state.DispatchersByNode.Count);
        Assert.True(state.DispatchersByNode[0].SetEquals(dispatcherOwnedBlocks));
        Assert.True(state.DispatchersByNode[1].SetEquals(dispatcherOwnedBlocks));
        Assert.Equal(1, loops.CallCount);
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var components = new UnusedControlFlowComponentAnalyzer();
        var naturalLoops = new UnusedControlFlowNaturalLoopAnalyzer();
        var domains = new UnusedControlFlowDomainFinder();
        var loops = new UnusedLoopRegionFactory();
        var cycles = new UnusedControlFlowCycleClassifier();
        var projection = new IdentityExceptionContinuationGraphProjector();

        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            null!, components, naturalLoops, domains, loops, cycles));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            projection, null!, naturalLoops, domains, loops, cycles));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            projection, components, null!, domains, loops, cycles));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            projection, components, naturalLoops, null!, loops, cycles));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            projection, components, naturalLoops, domains, null!, cycles));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            projection, components, naturalLoops, domains, loops, null!));
    }

    private sealed class IdentityExceptionContinuationGraphProjector : IExceptionContinuationGraphProjector
    {
        public ControlFlowGraph Project(ControlFlowGraph graph) => graph;
    }

    private sealed class RecordingProjection(ControlFlowGraph original, ControlFlowGraph projected)
        : IExceptionContinuationGraphProjector
    {
        public int CallCount { get; private set; }
        public ControlFlowGraph Project(ControlFlowGraph graph)
        {
            Assert.Same(original, graph);
            CallCount++;
            return projected;
        }
    }

    private sealed class RecordingDomains(ControlFlowGraph projected) : IControlFlowDomainFinder
    {
        public int CallCount { get; private set; }
        public ImmutableArray<ControlFlowDomain> Find(ControlFlowGraph graph)
        {
            Assert.Same(projected, graph);
            CallCount++;
            return [new(0, [0, 2, 3])];
        }
    }

    private sealed class RecordingNaturalLoops(ControlFlowGraph projected) : IControlFlowNaturalLoopAnalyzer
    {
        public int CallCount { get; private set; }
        public ImmutableArray<ImmutableHashSet<int>> Analyze(ControlFlowGraph graph, int entry,
            ImmutableHashSet<int> blocks)
        {
            Assert.Same(projected, graph);
            Assert.Equal(0, entry);
            Assert.True(blocks.SetEquals([0, 2, 3]));
            CallCount++;
            return [[0]];
        }
    }

    private sealed class RecordingComponents(ControlFlowGraph projected) : IControlFlowComponentAnalyzer
    {
        public int CallCount { get; private set; }
        public ImmutableArray<ImmutableHashSet<int>> Analyze(ControlFlowGraph graph, ImmutableHashSet<int> allowed)
        {
            Assert.Same(projected, graph);
            Assert.True(allowed.SetEquals([0, 2, 3]));
            CallCount++;
            return [[0], [2], [3]];
        }
    }

    private sealed class RecordingCycles(ControlFlowGraph projected) : IControlFlowCycleClassifier
    {
        public int CallCount { get; private set; }
        public bool Classify(ControlFlowGraph graph, ImmutableHashSet<int> component)
        {
            Assert.Same(projected, graph);
            CallCount++;
            return !component.Contains(3);
        }
    }

    private sealed class RecordingLoopFactory(ControlFlowGraph original, LoopRegion loop) : ILoopRegionFactory
    {
        public int CallCount { get; private set; }
        public LoopRegionCreation Create(ControlFlowStructuringState state, ControlFlowGraph graph,
            ImmutableHashSet<int> component, ControlFlowDomain domain, ImmutableArray<ControlFlowDomain> domains)
        {
            Assert.Same(original, state.Graph);
            Assert.Same(original, graph);
            Assert.True(component.SetEquals([0]));
            Assert.Same(domain, Assert.Single(domains));
            CallCount++;
            return new(loop, []);
        }
    }

    private sealed class UnusedControlFlowComponentAnalyzer : IControlFlowComponentAnalyzer
    {
        public ImmutableArray<ImmutableHashSet<int>> Analyze(
            ControlFlowGraph graph,
            ImmutableHashSet<int> allowed)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class EmptyControlFlowComponentAnalyzer : IControlFlowComponentAnalyzer
    {
        public ImmutableArray<ImmutableHashSet<int>> Analyze(
            ControlFlowGraph graph,
            ImmutableHashSet<int> allowed) => [];
    }

    private sealed class UnusedControlFlowNaturalLoopAnalyzer : IControlFlowNaturalLoopAnalyzer
    {
        public ImmutableArray<ImmutableHashSet<int>> Analyze(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> blocks)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedControlFlowNaturalLoopAnalyzer(
        ImmutableHashSet<int> component) : IControlFlowNaturalLoopAnalyzer
    {
        public ImmutableArray<ImmutableHashSet<int>> Analyze(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> blocks) => [component];
    }

    private sealed class UnusedControlFlowDomainFinder : IControlFlowDomainFinder
    {
        public ImmutableArray<ControlFlowDomain> Find(ControlFlowGraph graph)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedControlFlowDomainFinder(
        ControlFlowDomain domain) : IControlFlowDomainFinder
    {
        public ImmutableArray<ControlFlowDomain> Find(ControlFlowGraph graph) => [domain];
    }

    private sealed class UnusedLoopRegionFactory : ILoopRegionFactory
    {
        public LoopRegionCreation Create(
            ControlFlowStructuringState state,
            ControlFlowGraph graph,
            ImmutableHashSet<int> component,
            ControlFlowDomain domain,
            ImmutableArray<ControlFlowDomain> domains)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedLoopRegionFactory(
        LoopRegionCreation creation) : ILoopRegionFactory
    {
        public int CallCount { get; private set; }

        public LoopRegionCreation Create(
            ControlFlowStructuringState state,
            ControlFlowGraph graph,
            ImmutableHashSet<int> component,
            ControlFlowDomain domain,
            ImmutableArray<ControlFlowDomain> domains)
        {
            CallCount++;
            return creation;
        }
    }

    private sealed class UnusedControlFlowCycleClassifier : IControlFlowCycleClassifier
    {
        public bool Classify(ControlFlowGraph graph, ImmutableHashSet<int> component)
        {
            throw new NotSupportedException();
        }
    }
}
