// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class LoopEntryTargetFinderTests
{
    // Issue #259's shape: a loop in a finally (domain 1) whose body has a try at 3; the nested catch (domain 5) leaves to
    // the loop's increment at 4. 1 enters the loop's condition at 2; 2 enters the body at 3 or exits at 6.
    private static readonly ControlFlowGraph HandlerLoop = Graph(
        (0, []), (1, [2]), (2, [3, 6]), (3, [4]), (4, [2]), (5, [4]), (6, []));

    [Fact]
    public void ALoopInAHandlerIsEnteredOnlyFromItsHandler()
    {
        var finder = Assert.IsAssignableFrom<ILoopEntryTargetFinder>(new LoopEntryTargetFinder());
        var handler = new ControlFlowDomain(1, [1, 2, 3, 4, 6]);

        var entries = finder.Find(HandlerLoop, [2, 3, 4], handler, [new(0, [0]), handler, new(5, [5])]);

        Assert.Equal([2], entries);
    }

    [Fact]
    public void ALoopInTheMethodBodyIsEnteredOnlyFromTheBody()
    {
        var finder = new LoopEntryTargetFinder();
        var body = new ControlFlowDomain(0, [0, 1, 2, 3, 4, 6]);

        var entries = finder.Find(HandlerLoop, [2, 3, 4], body, [body, new(5, [5])]);

        Assert.Equal([2], entries);
    }

    [Fact]
    public void EveryBlockItsOwnDomainEntersFromOutsideTheLoopIsAnEntry()
    {
        var finder = new LoopEntryTargetFinder();
        var domain = new ControlFlowDomain(1, [1, 2, 3, 4, 5, 6]);

        var entries = finder.Find(HandlerLoop, [2, 3, 4], domain, [domain]);

        Assert.True(entries.SetEquals([2, 4]));
    }

    [Fact]
    public void AnUnreachablePredecessorDoesNotEnterTheLoop()
    {
        var finder = new LoopEntryTargetFinder();
        var graph = Graph([0, 1, 2, 3], (0, [1]), (1, [2]), (2, [1, 3]), (3, []), (4, [2]));
        var domain = new ControlFlowDomain(0, [0, 1, 2, 3, 4]);

        var entries = finder.Find(graph, [1, 2], domain, [domain]);

        Assert.Equal([1], entries);
    }

    private static ControlFlowGraph Graph(params (int Block, int[] Successors)[] edges) =>
        Graph([.. edges.Select(edge => edge.Block)], edges);

    private static ControlFlowGraph Graph(ImmutableHashSet<int> reachable, params (int Block, int[] Successors)[] edges)
    {
        var successors = edges.ToImmutableDictionary(edge => edge.Block, edge => edge.Successors.ToImmutableArray());
        var predecessors = edges.ToImmutableDictionary(edge => edge.Block, edge => edges
            .Where(other => other.Successors.Contains(edge.Block))
            .Select(other => other.Block)
            .ToImmutableArray());
        var empty = edges.ToImmutableDictionary(edge => edge.Block, _ => ImmutableArray<int>.Empty);
        return new ControlFlowGraph(
            ControlFlowStructuringStateTestFactory.Create().Graph.MethodBody,
            [.. edges.Select(edge => new BasicBlock(edge.Block, edge.Block, []))],
            successors,
            predecessors,
            empty,
            empty,
            reachable);
    }
}
