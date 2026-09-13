using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowComponentAnalyzer : IControlFlowComponentAnalyzer
{
    public ImmutableArray<ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        ImmutableHashSet<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(allowed);
        var nextIndex = 0;
        var indices = new Dictionary<int, int>();
        var lowLinks = new Dictionary<int, int>();
        var stack = new Stack<int>();
        var onStack = new HashSet<int>();
        var components = ImmutableArray.CreateBuilder<ImmutableHashSet<int>>();

        void Visit(int node)
        {
            indices[node] = nextIndex;
            lowLinks[node] = nextIndex;
            nextIndex++;
            stack.Push(node);
            onStack.Add(node);
            foreach (var successor in graph.Successors[node])
            {
                if (!allowed.Contains(successor))
                {
                    continue;
                }
                if (!indices.TryGetValue(successor, out var successorIndex))
                {
                    Visit(successor);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[successor]);
                }
                else if (onStack.Contains(successor))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], successorIndex);
                }
            }
            if (lowLinks[node] != indices[node])
            {
                return;
            }
            var component = ImmutableHashSet.CreateBuilder<int>();
            int member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
            }
            while (member != node);
            components.Add(component.ToImmutable());
        }

        foreach (var node in allowed.Order())
        {
            if (!indices.ContainsKey(node))
            {
                Visit(node);
            }
        }
        return components.ToImmutable();
    }
}
