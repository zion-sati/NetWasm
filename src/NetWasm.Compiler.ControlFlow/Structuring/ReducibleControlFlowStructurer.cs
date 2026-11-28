// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IReducibleControlFlowStructurer
{
    // The method's control flow as labeled blocks, loops and branches, or null
    // when the method has exception regions or irreducible control flow. With a
    // range, the range's blocks alone (a protected block's or a handler's),
    // whose edges out of the range end their blocks; null too when its entry
    // does not reach every reachable block of it, or when it ends a finally,
    // fault or filter handler.
    StructuredSequenceDraft? Structure(ValidatedControlFlowGraph validated, StructuringRange? range = null);
}

// Places every block once, without a dispatcher, by walking the dominator
// tree (N. Ramsey, "Beyond Relooper", ICFP 2022): a block with one forward
// predecessor follows it inline; a block with more is named by a labeled block
// placed in its immediate dominator, and its predecessors branch to that
// label; a loop header's body is a labeled loop that its back edges branch to.
// A block reached by leaving loops is placed after the outermost loop it
// leaves, so code after a loop does not nest inside it. A loop whose header
// tests whether to enter its body is a pre-test loop instead, which emission
// rotates, and a diamond of single blocks stays an if/else that Binaryen can
// turn into a select. An else-if chain the policy flattens keeps its
// conditions in one sequence, whichever arm of each continues it.
internal sealed class ReducibleControlFlowStructurer(
    ICilConditionalBranchClassifier conditionalBranches,
    IElseIfChainPolicy elseIfChains) :
    IReducibleControlFlowStructurer
{
    private readonly ICilConditionalBranchClassifier _conditionalBranches =
        conditionalBranches ?? throw new ArgumentNullException(nameof(conditionalBranches));
    private readonly IElseIfChainPolicy _elseIfChains =
        elseIfChains ?? throw new ArgumentNullException(nameof(elseIfChains));

    public StructuredSequenceDraft? Structure(ValidatedControlFlowGraph validated, StructuringRange? range = null)
    {
        ArgumentNullException.ThrowIfNull(validated);
        if (range is null && !validated.Graph.MethodBody.ExceptionRegions.IsEmpty)
        {
            return null;
        }
        var view = new View(validated.Graph, range);
        if (view.EndsAHandler())
        {
            return null;
        }
        return Analysis.Create(validated, view, _conditionalBranches, _elseIfChains) is { } analysis
            ? new StructuredSequenceDraft(analysis.Tree(view.Entry))
            : null;
    }

    // The method's graph, or a range of it whose edges leaving the range are
    // dropped: a block that ends in such an edge ends the range's control flow.
    private sealed class View
    {
        private readonly ImmutableHashSet<int>? _blocks;
        private readonly Dictionary<int, ImmutableArray<int>> _successors = [];

        internal View(ControlFlowGraph graph, StructuringRange? range)
        {
            Graph = graph;
            _blocks = range?.Blocks;
            Entry = range?.Entry ?? graph.Entry.Index;
        }

        internal ControlFlowGraph Graph { get; }

        internal int Entry { get; }

        internal ImmutableArray<int> Successors(int block)
        {
            if (_blocks is null)
            {
                return Graph.Successors[block];
            }
            if (!_successors.TryGetValue(block, out var within))
            {
                _successors[block] = within = [.. Graph.Successors[block].Where(_blocks.Contains)];
            }
            return within;
        }

        internal IEnumerable<int> Predecessors(int block) =>
            _blocks is null ? Graph.Predecessors[block] : Graph.Predecessors[block].Where(_blocks.Contains);

        // Whether the entry reached every reachable block: a range's blocks may
        // also be reached only from outside it, which this view cannot place.
        internal bool Covers(int reached) => _blocks is null || reached == _blocks.Count(Graph.ReachableBlocks.Contains);

        // Whether a block of the range ends a finally, fault or filter handler. Control leaves such a handler by
        // falling out of its end, for which emission writes no branch, so its last block must stay last in its
        // sequence; labels may place it inside an arm, where control would fall into the code after the arm.
        internal bool EndsAHandler() =>
            _blocks is not null && _blocks.Any(block => Graph.GetBlock(block).Terminator.Operation is
                CilOperation.EndFinally or CilOperation.EndFilter);
    }

    private sealed class Analysis
    {
        private readonly View _view;
        private readonly ControlFlowGraph _graph;
        private readonly ICilConditionalBranchClassifier _conditionalBranches;
        private readonly IElseIfChainPolicy _elseIfChains;
        private readonly Dictionary<int, int> _chainLengths = [];
        private readonly Dictionary<int, int> _order;
        private readonly Dictionary<int, int> _dominators;
        private readonly Dictionary<int, HashSet<int>> _loops;
        private readonly HashSet<int> _labeled = [];
        private readonly Dictionary<int, List<int>> _placed = [];
        private readonly Dictionary<int, PreTest> _preTested = [];

        private Analysis(
            View view,
            ICilConditionalBranchClassifier conditionalBranches,
            IElseIfChainPolicy elseIfChains,
            Dictionary<int, int> order,
            Dictionary<int, int> dominators,
            Dictionary<int, HashSet<int>> loops)
        {
            _view = view;
            _graph = view.Graph;
            _conditionalBranches = conditionalBranches;
            _elseIfChains = elseIfChains;
            _order = order;
            _dominators = dominators;
            _loops = loops;
        }

        internal static Analysis? Create(
            ValidatedControlFlowGraph validated,
            View view,
            ICilConditionalBranchClassifier conditionalBranches,
            IElseIfChainPolicy elseIfChains)
        {
            var postorder = Postorder(view);
            if (!view.Covers(postorder.Count))
            {
                return null;
            }
            var order = new Dictionary<int, int>();
            for (var index = 0; index < postorder.Count; index++)
            {
                order[postorder[postorder.Count - 1 - index]] = index;
            }
            var dominators = Dominators(view, postorder, order);
            var loops = new Dictionary<int, HashSet<int>>();
            foreach (var block in order.Keys)
            {
                foreach (var successor in view.Successors(block))
                {
                    if (order[successor] > order[block])
                    {
                        continue;
                    }
                    // A retreating edge whose target does not dominate its
                    // source enters a cycle other than at its header.
                    if (!Dominates(dominators, successor, block))
                    {
                        return null;
                    }
                    if (!loops.TryGetValue(successor, out var body))
                    {
                        loops[successor] = body = [successor];
                    }
                    AddNaturalLoop(view, order, body, block);
                }
            }
            var analysis = new Analysis(view, conditionalBranches, elseIfChains, order, dominators, loops);
            analysis.Place();
            analysis.SelectPreTests(validated);
            return analysis;
        }

        // The regions for the block and every block it places, in one sequence.
        internal ImmutableArray<StructuredRegionDraft> Tree(int block)
        {
            var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
            Expand(block, regions);
            return regions.ToImmutable();
        }

        private void Expand(int? next, ImmutableArray<StructuredRegionDraft>.Builder regions)
        {
            while (next is int block)
            {
                next = AppendTree(block, regions);
            }
        }

        // Appends the block's tree and returns the block whose tree follows it
        // in the same sequence, if any.
        private int? AppendTree(int block, ImmutableArray<StructuredRegionDraft>.Builder regions)
        {
            var labeled = _placed.TryGetValue(block, out var placed) ? placed : [];
            if (!_loops.TryGetValue(block, out var loop))
            {
                return AppendWithin(labeled, regions, inner => AppendCode(block, inner));
            }
            // Targets inside the loop are labeled within its body, and those it
            // leaves to around it.
            var body = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
            var preTested = _preTested.TryGetValue(block, out var test);
            Expand(
                AppendWithin(
                    [.. labeled.Where(loop.Contains)],
                    body,
                    // A pre-test loop evaluates the header itself; its body
                    // starts at the header's successor inside the loop.
                    inner => preTested ? Branch(block, test!.Body, inner) : AppendCode(block, inner)),
                body);
            StructuredRegionDraft region;
            if (preTested)
            {
                var exit = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
                Branch(block, test!.Exit, exit);
                region = new StructuredLoopDraft(
                    _graph.GetBlock(block),
                    test.ContinueWhenConditionTrue,
                    new StructuredSequenceDraft(body.ToImmutable()),
                    StructuredSequenceDraft.Empty,
                    new StructuredSequenceDraft(exit.ToImmutable()));
            }
            else
            {
                region = new StructuredLabeledLoopDraft(block, new StructuredSequenceDraft(body.ToImmutable()));
            }
            return AppendWithin(
                [.. labeled.Where(target => !loop.Contains(target))],
                regions,
                inner =>
                {
                    inner.Add(region);
                    return null;
                });
        }

        // Wraps the core in a labeled block for each target, innermost (first
        // in reverse postorder) first, each followed by its target's tree; the
        // outermost target's tree is returned to follow in the caller's
        // sequence.
        private int? AppendWithin(
            List<int> targets,
            ImmutableArray<StructuredRegionDraft>.Builder regions,
            Func<ImmutableArray<StructuredRegionDraft>.Builder, int?> core)
        {
            if (targets.Count == 0)
            {
                return core(regions);
            }
            var inner = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
            Expand(core(inner), inner);
            foreach (var target in targets[..^1])
            {
                var wrapped = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
                wrapped.Add(new StructuredLabeledBlockDraft(target, new StructuredSequenceDraft(inner.ToImmutable())));
                Expand(target, wrapped);
                inner = wrapped;
            }
            regions.Add(new StructuredLabeledBlockDraft(targets[^1], new StructuredSequenceDraft(inner.ToImmutable())));
            return targets[^1];
        }

        private int? AppendCode(int block, ImmutableArray<StructuredRegionDraft>.Builder regions)
        {
            var basicBlock = _graph.GetBlock(block);
            var successors = _view.Successors(block);
            if (successors.Length == 0)
            {
                regions.Add(new StructuredBlockDraft(basicBlock));
                return null;
            }
            if (successors.Length == 1)
            {
                // A conditional branch to the next instruction still evaluates
                // its condition, which an if with empty arms consumes.
                regions.Add(_conditionalBranches.Classify(basicBlock.Terminator.Operation)
                    ? new StructuredIfDraft(basicBlock, StructuredSequenceDraft.Empty, StructuredSequenceDraft.Empty)
                    : new StructuredBlockDraft(basicBlock));
                return Branch(block, successors[0], regions);
            }
            var (whenTrue, whenFalse) = Arms(basicBlock, successors);
            if (Join(block, whenTrue) is int join && Join(block, whenFalse) == join)
            {
                regions.Add(new StructuredIfDraft(
                    basicBlock,
                    JoinArm(block, whenTrue, join),
                    JoinArm(block, whenFalse, join)));
                return null;
            }
            // Both arms end in a transfer, so one goes in the if and the other
            // follows it: a lone branch inside, the larger arm after.
            if (IsLabelBranch(block, whenFalse) && !IsLabelBranch(block, whenTrue))
            {
                var branch = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
                Branch(block, whenFalse, branch);
                regions.Add(new StructuredIfDraft(
                    basicBlock,
                    StructuredSequenceDraft.Empty,
                    new StructuredSequenceDraft(branch.ToImmutable())));
                return whenTrue;
            }
            // The arm that goes in the if is the true one, unless that arm starts the next condition of an else-if
            // chain the policy flattens: then the false arm goes in and the chain continues in this sequence, as it
            // does when its false arms continue it, instead of nesting a level per condition.
            var inside = ChainNext(block) == whenTrue && _elseIfChains.Flattens(ChainLength(block))
                ? whenFalse
                : whenTrue;
            var arm = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
            Expand(Branch(block, inside, arm), arm);
            var armSequence = new StructuredSequenceDraft(arm.ToImmutable());
            regions.Add(inside == whenTrue
                ? new StructuredIfDraft(basicBlock, armSequence, StructuredSequenceDraft.Empty)
                : new StructuredIfDraft(basicBlock, StructuredSequenceDraft.Empty, armSequence));
            return Branch(block, inside == whenTrue ? whenFalse : whenTrue, regions);
        }

        // The arm of a two-way branch that starts the next condition of an else-if chain: a two-way branch that
        // follows it inline, as its only forward predecessor's, with no loop or labeled target of its own. Null when
        // neither arm or both do. Either arm may go in the if, so this only picks which one does.
        private int? ChainNext(int block)
        {
            var successors = _view.Successors(block);
            var first = Continues(successors[0]);
            return first == Continues(successors[1]) ? null : first ? successors[0] : successors[1];

            bool Continues(int arm) =>
                !IsLabelBranch(block, arm) && !_loops.ContainsKey(arm) && !_placed.ContainsKey(arm) &&
                _view.Successors(arm).Length == 2;
        }

        // The conditions in the chain the block is in that start the next one, counted once from its first.
        private int ChainLength(int block)
        {
            if (_chainLengths.TryGetValue(block, out var length))
            {
                return length;
            }
            var links = new List<int>();
            for (int? link = block; link is int current && ChainNext(current) is int next; link = next)
            {
                links.Add(current);
            }
            foreach (var link in links)
            {
                _chainLengths[link] = links.Count;
            }
            return links.Count;
        }

        // The branch target is successors[0]; the condition value is true when
        // it is taken, except for brfalse.
        private static (int WhenTrue, int WhenFalse) Arms(BasicBlock block, ImmutableArray<int> successors) =>
            block.Terminator.Operation == CilOperation.BranchIfFalse
                ? (successors[1], successors[0])
                : (successors[0], successors[1]);

        // Where an arm of a diamond or triangle rejoins: a labeled target the
        // arm branches to, or the one target of a single block that follows
        // inline.
        private int? Join(int source, int arm)
        {
            if (IsLabelBranch(source, arm))
            {
                return arm;
            }
            var successors = _view.Successors(arm);
            return !_loops.ContainsKey(arm) && !_placed.ContainsKey(arm) &&
                successors.Length == 1 && IsLabelBranch(arm, successors[0])
                ? successors[0]
                : null;
        }

        private StructuredSequenceDraft JoinArm(int source, int arm, int join)
        {
            var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
            if (IsLabelBranch(source, arm))
            {
                Branch(source, arm, regions);
            }
            else
            {
                regions.Add(new StructuredBlockDraft(_graph.GetBlock(arm)));
                Branch(arm, join, regions);
            }
            return new StructuredSequenceDraft(regions.ToImmutable());
        }

        // A branch to a labeled target, a pre-test loop's next iteration, or
        // the target itself to follow inline.
        private int? Branch(int source, int target, ImmutableArray<StructuredRegionDraft>.Builder regions)
        {
            if (!IsLabelBranch(source, target))
            {
                return target;
            }
            regions.Add(_order[target] <= _order[source] && _preTested.ContainsKey(target)
                ? new StructuredLoopContinueDraft()
                : new StructuredLabelBranchDraft(target));
            return null;
        }

        // A header ending in a branch into its loop or out of it, entered with
        // an empty stack, is a pre-test loop. A continue reaches the innermost
        // loop's condition, so every back edge must come from outside the
        // header's inner loops; the exit is reached by a branch, as every
        // loop exit is.
        private void SelectPreTests(ValidatedControlFlowGraph validated)
        {
            foreach (var (header, body) in _loops)
            {
                var successors = _view.Successors(header);
                var inside = successors.Where(body.Contains).ToArray();
                if (successors.Length != 2 || inside.Length != 1 ||
                    !validated.EntryStacks[header].IsEmpty)
                {
                    continue;
                }
                var exit = successors[0] == inside[0] ? successors[1] : successors[0];
                var latches = _view.Predecessors(header)
                    .Where(block => _order.TryGetValue(block, out var order) && order >= _order[header]);
                if (!IsLabelBranch(header, exit) || latches.Any(latch => _loops.Any(inner =>
                        inner.Key != header && body.Contains(inner.Key) && inner.Value.Contains(latch))))
                {
                    continue;
                }
                _preTested[header] = new(inside[0], exit, Arms(_graph.GetBlock(header), successors).WhenTrue == inside[0]);
            }
        }

        private bool IsLabelBranch(int source, int target) =>
            _order[target] <= _order[source] || _labeled.Contains(target);

        // Decides where each block is placed, filling each placement list in
        // reverse postorder. A block with more than one
        // forward predecessor, or reached by leaving a loop that contains its
        // immediate dominator, is labeled; the others follow their only
        // forward predecessor, which is their immediate dominator.
        private void Place()
        {
            var nesting = _loops.OrderByDescending(loop => loop.Value.Count).ToArray();
            var forwardPredecessors = new Dictionary<int, int>();
            foreach (var block in _order.Keys)
            {
                foreach (var successor in _view.Successors(block))
                {
                    if (_order[successor] > _order[block])
                    {
                        forwardPredecessors[successor] = forwardPredecessors.GetValueOrDefault(successor) + 1;
                    }
                }
            }
            foreach (var block in _order.Keys.OrderBy(block => _order[block]))
            {
                if (block == _view.Entry)
                {
                    continue;
                }
                var parent = _dominators[block];
                var exits = false;
                foreach (var (header, body) in nesting)
                {
                    if (body.Contains(parent) && !body.Contains(block))
                    {
                        parent = header;
                        exits = true;
                        break;
                    }
                }
                if (!exits && forwardPredecessors.GetValueOrDefault(block) < 2)
                {
                    continue;
                }
                _labeled.Add(block);
                if (!_placed.TryGetValue(parent, out var placed))
                {
                    _placed[parent] = placed = [];
                }
                placed.Add(block);
            }
        }

        private sealed record PreTest(int Body, int Exit, bool ContinueWhenConditionTrue);

        private static List<int> Postorder(View view)
        {
            var postorder = new List<int>();
            var visited = new HashSet<int> { view.Entry };
            var stack = new Stack<(int Block, int Next)>();
            stack.Push((view.Entry, 0));
            while (stack.TryPop(out var frame))
            {
                var successors = view.Successors(frame.Block);
                if (frame.Next < successors.Length)
                {
                    stack.Push((frame.Block, frame.Next + 1));
                    if (visited.Add(successors[frame.Next]))
                    {
                        stack.Push((successors[frame.Next], 0));
                    }
                    continue;
                }
                postorder.Add(frame.Block);
            }
            return postorder;
        }

        // Immediate dominators (K. Cooper, T. Harvey and K. Kennedy, "A Simple,
        // Fast Dominance Algorithm"); the entry is its own.
        private static Dictionary<int, int> Dominators(
            View view,
            List<int> postorder,
            Dictionary<int, int> order)
        {
            var entry = view.Entry;
            var dominators = new Dictionary<int, int> { [entry] = entry };
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var index = postorder.Count - 1; index >= 0; index--)
                {
                    var block = postorder[index];
                    if (block == entry)
                    {
                        continue;
                    }
                    int? dominator = null;
                    foreach (var predecessor in view.Predecessors(block))
                    {
                        if (!dominators.ContainsKey(predecessor))
                        {
                            continue;
                        }
                        dominator = dominator is int current
                            ? Intersect(dominators, order, predecessor, current)
                            : predecessor;
                    }
                    if (dominator is int value &&
                        (!dominators.TryGetValue(block, out var previous) || previous != value))
                    {
                        dominators[block] = value;
                        changed = true;
                    }
                }
            }
            return dominators;
        }

        private static int Intersect(Dictionary<int, int> dominators, Dictionary<int, int> order, int left, int right)
        {
            while (left != right)
            {
                while (order[left] > order[right])
                {
                    left = dominators[left];
                }
                while (order[right] > order[left])
                {
                    right = dominators[right];
                }
            }
            return left;
        }

        private static bool Dominates(Dictionary<int, int> dominators, int dominator, int block)
        {
            while (true)
            {
                if (block == dominator)
                {
                    return true;
                }
                var parent = dominators[block];
                if (parent == block)
                {
                    return false;
                }
                block = parent;
            }
        }

        // The blocks that reach the back edge's source without passing the
        // header, which the body already holds.
        private static void AddNaturalLoop(
            View view,
            Dictionary<int, int> order,
            HashSet<int> body,
            int source)
        {
            var pending = new Stack<int>();
            if (body.Add(source))
            {
                pending.Push(source);
            }
            while (pending.TryPop(out var block))
            {
                foreach (var predecessor in view.Predecessors(block))
                {
                    if (order.ContainsKey(predecessor) && body.Add(predecessor))
                    {
                        pending.Push(predecessor);
                    }
                }
            }
        }
    }
}
