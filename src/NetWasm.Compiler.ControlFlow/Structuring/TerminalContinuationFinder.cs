// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// A continuation of a protected block that cannot complete normally: the blocks it reaches from its target are all at
// or after the target and before the end, every successor of each is among them, each ends in a return, a throw or a
// branch among them (never a leave, an endfinally or an endfilter), none starts a protected block or a handler, and no
// other continuation of the block reaches any of them. Emitted after the protected block behind a test of the leave
// that chose it, it ends in a return or a throw on every path, so it never runs on into the next continuation, as a
// continuation of straight-line blocks ending in a return does. Without it, one such continuation with a branch (a
// method's suspension exit testing whether a context changed) sent all of the block's continuations through a
// dispatcher.
internal sealed class TerminalContinuationFinder : ITerminalContinuationFinder
{
    public ImmutableHashSet<int>? Find(ControlFlowGraph graph, int target, int end, IReadOnlyCollection<int> otherTargets)
    {
        var entry = graph.GetBlockAtOffset(target).Index;
        var blocks = new HashSet<int> { entry };
        var pending = new Stack<int>([entry]);
        while (pending.TryPop(out var block))
        {
            var basic = graph.GetBlock(block);
            if (basic.StartOffset < target || basic.StartOffset >= end ||
                basic.Terminator.Operation is CilOperation.Leave or CilOperation.EndFinally or CilOperation.EndFilter ||
                graph.ExceptionalSuccessors[block].Length != 0 ||
                graph.Successors[block].IsEmpty &&
                basic.Terminator.Operation is not (CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow))
            {
                return null;
            }
            foreach (var successor in graph.Successors[block])
            {
                if (blocks.Add(successor)) pending.Push(successor);
            }
        }
        if (blocks.Any(block => graph.RegionBlocks.RegionStartOffsets.Contains(graph.GetBlock(block).StartOffset)))
        {
            return null;
        }
        foreach (var other in otherTargets)
        {
            var start = graph.GetBlockAtOffset(other).Index;
            var seen = new HashSet<int> { start };
            var walk = new Stack<int>([start]);
            while (walk.TryPop(out var block))
            {
                if (blocks.Contains(block)) return null;
                foreach (var successor in graph.Successors[block])
                {
                    if (seen.Add(successor)) walk.Push(successor);
                }
            }
        }
        return [.. blocks];
    }
}
