using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ControlFlowStructuringStateBuilderTests
{
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

        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            null!, naturalLoops, domains, loops, cycles));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            components, null!, domains, loops, cycles));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            components, naturalLoops, null!, loops, cycles));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            components, naturalLoops, domains, null!, cycles));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowStructuringStateBuilder(
            components, naturalLoops, domains, loops, null!));
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
