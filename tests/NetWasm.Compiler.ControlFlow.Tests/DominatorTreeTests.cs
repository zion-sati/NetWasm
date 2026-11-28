// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests;

// The analyzers answer from a dominator tree, linear in the blocks, what the iterative analyses they replaced
// answered with a set per block: the same dominators for every block, over loops, irreducible flow, several exits,
// blocks the entry does not reach and subsets of a method's blocks.
public sealed class DominatorTreeTests
{
    [Fact]
    public void DominanceMatchesTheSetBasedFixedPoint()
    {
        var random = new Random(20261119);
        for (var round = 0; round < 400; round++)
        {
            var graph = ControlFlowGraphBuilder.Build(RandomBody(random, random.Next(2, 40)));
            var blocks = Subset(random, graph);
            var entry = random.Next(3) == 0 ? blocks.ElementAt(random.Next(blocks.Count)) : graph.Entry.Index;

            var tree = new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder()).Analyze(graph, entry, blocks);

            AssertSame(ReferenceDominators(graph, entry, blocks), tree, $"round {round}");
        }
    }

    [Fact]
    public void PostDominanceMatchesTheSetBasedFixedPoint()
    {
        var random = new Random(20261120);
        for (var round = 0; round < 400; round++)
        {
            var graph = ControlFlowGraphBuilder.Build(RandomBody(random, random.Next(2, 40)));
            var allowed = Subset(random, graph);
            int? stop = random.Next(3) == 0 ? allowed.ElementAt(random.Next(allowed.Count)) : null;

            var tree = new ControlFlowPostDominanceAnalyzer(new DominatorTreeBuilder()).Analyze(graph, stop, allowed);

            AssertSame(ReferencePostDominators(graph, stop, allowed), tree, $"round {round}");
        }
    }

    // A chain of 100,000 blocks on a quarter-megabyte stack: every walk keeps its own stack.
    [Fact]
    public void AChainOfAHundredThousandBlocksNeedsNoDeepStack()
    {
        const int Branches = 50_000;
        var instructions = new List<CilInstruction>();
        for (var index = 0; index < Branches; index++)
        {
            var offset = instructions.Count;
            instructions.Add(I(offset, CilOperation.LoadArgument, new CilOperand.Index(0)));
            instructions.Add(I(offset + 1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(offset + 2)));
        }
        instructions.Add(I(instructions.Count, CilOperation.Return));
        var graph = ControlFlowGraphBuilder.Build(Body(CliValueKind.Void, 1, [], [.. instructions]));
        DominatorTree? dominators = null;
        DominatorTree? postDominators = null;

        var thread = new Thread(() =>
        {
            dominators = new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder()).Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
            postDominators = new ControlFlowPostDominanceAnalyzer(new DominatorTreeBuilder()).Analyze(graph, null, graph.ReachableBlocks);
        }, 256 * 1024);
        thread.Start();
        thread.Join();

        var last = graph.ReachableBlocks.Max();
        Assert.True(dominators!.Dominates(graph.Entry.Index, last));
        Assert.Equal(graph.ReachableBlocks.Count, dominators[last].Count());
        Assert.True(postDominators!.Dominates(last, graph.Entry.Index));
    }

    [Fact]
    public void ATreeAnswersItsQueries()
    {
        // 0 dominates 1 and 2; 1 dominates 3; 4 is a second root.
        var tree = DominatorTree.FromImmediateDominators([0, 1, 2, 3, 4],
            new Dictionary<int, int?> { [0] = null, [1] = 0, [2] = 0, [3] = 1, [4] = null });

        Assert.Equal([3, 1, 0], tree[3]);
        Assert.True(tree.Dominates(0, 3));
        Assert.True(tree.Dominates(3, 3));
        Assert.False(tree.Dominates(2, 3));
        Assert.False(tree.Dominates(0, 4));
        Assert.False(tree[3].Contains(7));
        Assert.Equal(1, tree.ImmediateDominator(3));
        Assert.Null(tree.ImmediateDominator(0));
        Assert.True(tree.TryGetValue(2, out var two));
        Assert.Equal([2, 0], two);
        Assert.False(tree.TryGetValue(7, out _));
        Assert.Throws<KeyNotFoundException>(() => tree[7]);
        Assert.Equal([0, 1, 2, 3, 4], tree.Blocks.Order());
        var untyped = new List<object>();
        foreach (var block in (System.Collections.IEnumerable)tree[3]) untyped.Add(block);
        Assert.Equal([3, 1, 0], untyped.Cast<int>());
    }

    // A block the analysis cannot reach is dominated by every block, and has no immediate dominator.
    [Fact]
    public void AnUnreachedBlockIsDominatedByEveryBlock()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Return),
            I(1, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(2, CilOperation.Branch, new CilOperand.BranchTarget(1))));
        var blocks = graph.Blocks.Select(block => block.Index).ToImmutableHashSet();

        var tree = new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder()).Analyze(graph, graph.Entry.Index, blocks);

        var loop = graph.Blocks.Single(block => block.StartOffset == 1).Index;
        Assert.True(tree.Dominates(graph.Entry.Index, loop));
        Assert.False(tree.Dominates(loop, graph.Entry.Index));
        Assert.Equal(blocks.Order(), tree[loop].Order());
        Assert.Null(tree.ImmediateDominator(loop));
    }

    [Fact]
    public void ItRejectsImmediateDominatorsThatFormNoTree()
    {
        Assert.Throws<ArgumentNullException>(() => DominatorTree.FromImmediateDominators(null!, new Dictionary<int, int?>()));
        Assert.Throws<ArgumentNullException>(() => DominatorTree.FromImmediateDominators([0], null!));
        Assert.Throws<ArgumentException>(() => DominatorTree.FromImmediateDominators([0, 1],
            new Dictionary<int, int?> { [0] = null }));
        Assert.Throws<ArgumentException>(() => DominatorTree.FromImmediateDominators([0],
            new Dictionary<int, int?> { [0] = 5 }));
        Assert.Throws<ArgumentException>(() => DominatorTree.FromImmediateDominators([0, 1, 2],
            new Dictionary<int, int?> { [0] = null, [1] = 2, [2] = 1 }));
    }

    [Fact]
    public void ASetOfNoTreeAnswersNothing()
    {
        var set = default(DominatorSet);

        Assert.Throws<InvalidOperationException>(() => set.Contains(0));
        Assert.Throws<InvalidOperationException>(() => set.ToArray());
    }

    [Fact]
    public void TheAnalyzersRejectMissingArguments()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowDominanceAnalyzer(null!));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowPostDominanceAnalyzer(null!));
        Assert.Throws<ArgumentNullException>(() => new DominatorTreeBuilder().Build(null!));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder()).Analyze(null!, 0, graph.ReachableBlocks));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder()).Analyze(graph, 0, null!));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowPostDominanceAnalyzer(new DominatorTreeBuilder()).Analyze(null!, null, graph.ReachableBlocks));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowPostDominanceAnalyzer(new DominatorTreeBuilder()).Analyze(graph, null, null!));
    }

    private static void AssertSame(Dictionary<int, HashSet<int>> expected, DominatorTree tree, string context)
    {
        Assert.True(expected.Keys.ToHashSet().SetEquals(tree.Blocks), context);
        foreach (var (block, dominators) in expected)
        {
            Assert.True(dominators.SetEquals(tree[block]), $"{context}, block {block}");
            foreach (var candidate in expected.Keys)
            {
                Assert.True(dominators.Contains(candidate) == tree[block].Contains(candidate),
                    $"{context}, block {block}, candidate {candidate}");
            }
        }
    }

    // Most rounds take the reachable blocks; some take a random subset of them, and some only blocks from the graph's
    // last third (a few blocks of a larger graph, which the tree numbers through a map rather than by index).
    private static ImmutableHashSet<int> Subset(Random random, ControlFlowGraph graph)
    {
        var all = graph.Blocks.Select(block => block.Index).ToImmutableHashSet();
        var subset = random.Next(5) switch
        {
            0 => graph.ReachableBlocks.Where(_ => random.Next(5) != 0).ToImmutableHashSet(),
            1 => all,
            2 => graph.ReachableBlocks.Where(block => block * 3 >= graph.Blocks.Length * 2).ToImmutableHashSet(),
            _ => graph.ReachableBlocks,
        };
        return subset.IsEmpty ? [graph.Entry.Index] : subset;
    }

    // The analyses the tree replaced, set by set: the maximal fixed point of Dom(n) = {n} ∪ ∩ Dom(p) over n's
    // predecessors within the blocks, the entry and a block without such a predecessor dominating only themselves.
    private static Dictionary<int, HashSet<int>> ReferenceDominators(ControlFlowGraph graph, int entry,
        ImmutableHashSet<int> blocks)
    {
        var sets = blocks.ToDictionary(block => block, block => block == entry ? new HashSet<int> { entry } : blocks.ToHashSet());
        bool changed;
        do
        {
            changed = false;
            foreach (var node in blocks.Where(block => block != entry).Order())
            {
                var predecessors = graph.Predecessors[node].Where(blocks.Contains).ToArray();
                var next = predecessors.Length == 0
                    ? new HashSet<int>()
                    : predecessors.Skip(1).Aggregate(sets[predecessors[0]].ToHashSet(), (set, predecessor) =>
                    {
                        set.IntersectWith(sets[predecessor]);
                        return set;
                    });
                next.Add(node);
                if (!next.SetEquals(sets[node]))
                {
                    sets[node] = next;
                    changed = true;
                }
            }
        }
        while (changed);
        return sets;
    }

    // PDom(n) = {n} ∪ ∩ PDom(s) over n's successors, the stop block, a block that leaves the allowed blocks and a
    // block with no allowed successor post-dominating only themselves.
    private static Dictionary<int, HashSet<int>> ReferencePostDominators(ControlFlowGraph graph, int? stop,
        ImmutableHashSet<int> allowed)
    {
        bool Ends(int node) => node == stop || graph.Successors[node].Any(successor => !allowed.Contains(successor)) ||
            !graph.Successors[node].Any(allowed.Contains);
        var sets = allowed.ToDictionary(block => block, block => Ends(block) ? new HashSet<int> { block } : allowed.ToHashSet());
        bool changed;
        do
        {
            changed = false;
            foreach (var node in allowed.Where(block => !Ends(block)).OrderDescending())
            {
                var successors = graph.Successors[node].ToArray();
                var next = successors.Skip(1).Aggregate(sets[successors[0]].ToHashSet(), (set, successor) =>
                {
                    set.IntersectWith(sets[successor]);
                    return set;
                });
                next.Add(node);
                if (!next.SetEquals(sets[node]))
                {
                    sets[node] = next;
                    changed = true;
                }
            }
        }
        while (changed);
        return sets;
    }

    // Straight runs, conditional branches forward and back, unconditional jumps and returns part way, ending in a
    // return; a branch goes to the start of a random statement, so some statements only a branch reaches.
    private static CilMethodBody RandomBody(Random random, int length)
    {
        var instructions = new List<CilInstruction>();
        var targets = new List<int>();
        for (var index = 0; index < length; index++)
        {
            var offset = instructions.Count;
            targets.Add(offset);
            switch (random.Next(6))
            {
                case 0:
                    instructions.Add(I(offset, CilOperation.Nop));
                    break;
                case 1:
                    instructions.Add(I(offset, CilOperation.LoadArgument, new CilOperand.Index(0)));
                    instructions.Add(I(offset + 1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(-1)));
                    break;
                case 2:
                    instructions.Add(I(offset, CilOperation.Branch, new CilOperand.BranchTarget(-1)));
                    break;
                case 3:
                    instructions.Add(I(offset, CilOperation.Return));
                    break;
                default:
                    instructions.Add(I(offset, CilOperation.LoadArgument, new CilOperand.Index(0)));
                    instructions.Add(I(offset + 1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(-1)));
                    break;
            }
        }
        var end = instructions.Count;
        targets.Add(end);
        instructions.Add(I(end, CilOperation.Return));
        return Body(CliValueKind.Void, 1, [], [.. instructions.Select(instruction =>
            instruction.Operand is CilOperand.BranchTarget
                ? instruction with { Operand = new CilOperand.BranchTarget(targets[random.Next(targets.Count)]) }
                : instruction)]);
    }
}
