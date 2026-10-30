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
    // when the method has exception regions, irreducible control flow or a
    // conditional branch whose two targets are one block.
    StructuredSequenceDraft? Structure(ValidatedControlFlowGraph validated);
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
// turn into a select.
internal sealed class ReducibleControlFlowStructurer : IReducibleControlFlowStructurer
{
    public StructuredSequenceDraft? Structure(ValidatedControlFlowGraph validated) =>
        validated.Graph.MethodBody.ExceptionRegions.IsEmpty && Analysis.Create(validated) is { } analysis
            ? new StructuredSequenceDraft(analysis.Tree(validated.Graph.Entry.Index))
            : null;

    private sealed class Analysis
    {
        private readonly ControlFlowGraph _graph;
        private readonly Dictionary<int, int> _order;
        private readonly Dictionary<int, int> _dominators;
        private readonly Dictionary<int, HashSet<int>> _loops;
        private readonly HashSet<int> _labeled = [];
        private readonly Dictionary<int, List<int>> _placed = [];
        private readonly Dictionary<int, PreTest> _preTested = [];

        private Analysis(
            ControlFlowGraph graph,
            Dictionary<int, int> order,
            Dictionary<int, int> dominators,
            Dictionary<int, HashSet<int>> loops)
        {
            _graph = graph;
            _order = order;
            _dominators = dominators;
            _loops = loops;
        }

        internal static Analysis? Create(ValidatedControlFlowGraph validated)
        {
            var graph = validated.Graph;
            var postorder = Postorder(graph);
            if (postorder.Any(block => !Supported(graph, block)))
            {
                return null;
            }
            var order = new Dictionary<int, int>();
            for (var index = 0; index < postorder.Count; index++)
            {
                order[postorder[postorder.Count - 1 - index]] = index;
            }
            var dominators = Dominators(graph, postorder, order);
            var loops = new Dictionary<int, HashSet<int>>();
            foreach (var block in order.Keys)
            {
                foreach (var successor in graph.Successors[block])
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
                    AddNaturalLoop(graph, order, body, block);
                }
            }
            var analysis = new Analysis(graph, order, dominators, loops);
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
            var successors = _graph.Successors[block];
            if (successors.Length == 0)
            {
                regions.Add(new StructuredBlockDraft(basicBlock));
                return null;
            }
            if (successors.Length == 1)
            {
                regions.Add(new StructuredBlockDraft(basicBlock));
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
            var arm = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
            Expand(Branch(block, whenTrue, arm), arm);
            regions.Add(new StructuredIfDraft(
                basicBlock,
                new StructuredSequenceDraft(arm.ToImmutable()),
                StructuredSequenceDraft.Empty));
            return Branch(block, whenFalse, regions);
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
            var successors = _graph.Successors[arm];
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
                var successors = _graph.Successors[header];
                var inside = successors.Where(body.Contains).ToArray();
                if (successors.Length != 2 || inside.Length != 1 ||
                    !validated.EntryStacks[header].IsEmpty)
                {
                    continue;
                }
                var exit = successors[0] == inside[0] ? successors[1] : successors[0];
                var latches = _graph.Predecessors[header]
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
                foreach (var successor in _graph.Successors[block])
                {
                    if (_order[successor] > _order[block])
                    {
                        forwardPredecessors[successor] = forwardPredecessors.GetValueOrDefault(successor) + 1;
                    }
                }
            }
            foreach (var block in _order.Keys.OrderBy(block => _order[block]))
            {
                if (block == _graph.Entry.Index)
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

        private static bool Supported(ControlFlowGraph graph, int block)
        {
            var terminator = graph.GetBlock(block).Terminator.Operation;
            var successors = graph.Successors[block].Length;
            return terminator switch
            {
                CilOperation.Leave or CilOperation.EndFinally or CilOperation.EndFilter or
                    CilOperation.Switch => false,
                CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
                    CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                    CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
                    CilOperation.BranchIfGreaterThanOrEqualSigned or
                    CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                    CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
                    CilOperation.BranchIfLessThanOrEqualSigned or
                    CilOperation.BranchIfLessThanOrEqualUnsigned => successors == 2,
                _ => successors <= 1,
            };
        }

        private static List<int> Postorder(ControlFlowGraph graph)
        {
            var postorder = new List<int>();
            var visited = new HashSet<int> { graph.Entry.Index };
            var stack = new Stack<(int Block, int Next)>();
            stack.Push((graph.Entry.Index, 0));
            while (stack.TryPop(out var frame))
            {
                var successors = graph.Successors[frame.Block];
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
            ControlFlowGraph graph,
            List<int> postorder,
            Dictionary<int, int> order)
        {
            var entry = graph.Entry.Index;
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
                    foreach (var predecessor in graph.Predecessors[block])
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
            ControlFlowGraph graph,
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
                foreach (var predecessor in graph.Predecessors[block])
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
