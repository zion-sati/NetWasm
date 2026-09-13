using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class LoopExitExtensionCollector : ILoopExitExtensionCollector
{
    public ImmutableHashSet<int> Collect(
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        int primaryExit,
        ImmutableHashSet<int> additionalExits)
    {
        var extension = ImmutableHashSet.CreateBuilder<int>();
        var pending = new Queue<int>(additionalExits.Order());
        while (pending.TryDequeue(out var node))
        {
            if (node == primaryExit || component.Contains(node) || !extension.Add(node))
            {
                continue;
            }
            foreach (var successor in graph.Successors[node])
            {
                if (successor != primaryExit && !component.Contains(successor))
                {
                    pending.Enqueue(successor);
                }
            }
        }
        return extension.ToImmutable();
    }
}
