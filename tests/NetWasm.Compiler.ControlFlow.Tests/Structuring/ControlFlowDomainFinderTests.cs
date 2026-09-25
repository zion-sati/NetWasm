using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ControlFlowDomainFinderTests
{
    [Fact]
    public void ConstructorRejectsNullReachability()
    {
        Assert.Throws<ArgumentNullException>(() => new ControlFlowDomainFinder(null!));
    }

    [Fact]
    public void FindIncludesHandlerOnlyContinuationsOutsideTheHandlerRange()
    {
        var graph = Graph(new CilExceptionRegion(CilExceptionRegionKind.Catch, 0, 1, 1, 1, TypeKey, null));
        var reachable = new RecordingReachableBlocks([0], [1, 2, 3]);
        var finder = Assert.IsAssignableFrom<IControlFlowDomainFinder>(new ControlFlowDomainFinder(reachable));

        var domains = finder.Find(graph);

        Assert.Equal(2, domains.Length);
        Assert.Equal(0, domains[0].Entry);
        Assert.True(domains[0].Blocks.SetEquals([0]));
        Assert.Equal(1, domains[1].Entry);
        Assert.True(domains[1].Blocks.SetEquals([1, 2, 3]));
        Assert.True(reachable.Allowed[0].SetEquals([0, 1, 2, 3]));
        Assert.True(reachable.Allowed[1].SetEquals([1, 2, 3]));
    }

    [Fact]
    public void FindAssignsSharedContinuationsToOnlyOneDomain()
    {
        var graph = Graph(new CilExceptionRegion(CilExceptionRegionKind.Catch, 0, 1, 1, 1, TypeKey, null));
        var reachable = new RecordingReachableBlocks([0, 2, 3], [1]);
        var finder = Assert.IsAssignableFrom<IControlFlowDomainFinder>(new ControlFlowDomainFinder(reachable));

        var domains = finder.Find(graph);

        Assert.True(domains[0].Blocks.SetEquals([0, 2, 3]));
        Assert.True(domains[1].Blocks.SetEquals([1]));
        Assert.True(reachable.Allowed[1].SetEquals([1]));
        Assert.False(domains[0].Blocks.Overlaps(domains[1].Blocks));
    }

    [Fact]
    public void FindAnalyzesFiltersAsIndependentRoots()
    {
        var graph = Graph(new CilExceptionRegion(CilExceptionRegionKind.Filter, 0, 1, 2, 1, null, 1));
        var reachable = new RecordingReachableBlocks([0], [2, 3], [1]);
        var finder = Assert.IsAssignableFrom<IControlFlowDomainFinder>(new ControlFlowDomainFinder(reachable));

        var domains = finder.Find(graph);

        Assert.Equal([0, 2, 1], domains.Select(domain => domain.Entry));
        Assert.True(domains[1].Blocks.SetEquals([2, 3]));
        Assert.True(domains[2].Blocks.SetEquals([1]));
        Assert.True(reachable.Allowed[2].SetEquals([1]));
    }

    [Fact]
    public void FindHandlesAMethodWithoutExceptionRegions()
    {
        var graph = Graph();
        var finder = Assert.IsAssignableFrom<IControlFlowDomainFinder>(new ControlFlowDomainFinder(
            new RecordingReachableBlocks([0, 1, 2, 3])));

        var domain = Assert.Single(finder.Find(graph));

        Assert.Equal(0, domain.Entry);
        Assert.Equal(graph.ReachableBlocks, domain.Blocks);
    }

    private static ControlFlowGraph Graph(params CilExceptionRegion[] regions) => new(
        Body(CliValueKind.Void, 0, []) with { ExceptionRegions = [.. regions] },
        [.. Enumerable.Range(0, 4).Select(index => new BasicBlock(index, index, []))],
        ImmutableDictionary<int, ImmutableArray<int>>.Empty,
        ImmutableDictionary<int, ImmutableArray<int>>.Empty,
        ImmutableDictionary<int, ImmutableArray<int>>.Empty,
        ImmutableDictionary<int, ImmutableArray<int>>.Empty,
        [0, 1, 2, 3]);

    private sealed class RecordingReachableBlocks(params ImmutableHashSet<int>[] results) : IReachableBlockFinder
    {
        public List<ImmutableHashSet<int>> Allowed { get; } = [];

        public ImmutableHashSet<int> Find(ControlFlowGraph graph, int entry, ImmutableHashSet<int> allowed)
        {
            Allowed.Add(allowed);
            return results[Allowed.Count - 1];
        }

        public ImmutableHashSet<int> Find(ControlFlowStructuringState state, int start, int? stop,
            ImmutableHashSet<int> allowed) => throw new NotSupportedException();

        public ImmutableHashSet<int> Find(ControlFlowGraph graph, int entry,
            ImmutableHashSet<int> stops, ImmutableHashSet<int> allowed) => throw new NotSupportedException();
    }
}
