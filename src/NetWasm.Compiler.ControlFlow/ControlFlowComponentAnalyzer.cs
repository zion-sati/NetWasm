// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        // Tarjan's algorithm with an explicit stack of (node, next successor) frames instead of recursion: a
        // path through a method's blocks is as long as the method, thousands of blocks for a long if/else-if
        // chain, which recursion would hold on the thread's stack. Nodes and successors are visited in the
        // recursive order, so the components come out in the same order.
        var frames = new Stack<(int Node, int Successor)>();
        foreach (var root in allowed.Order())
        {
            if (indices.ContainsKey(root))
            {
                continue;
            }
            Enter(root);
            while (frames.Count != 0)
            {
                var (node, successorIndex) = frames.Pop();
                var successors = graph.Successors[node];
                var descended = false;
                while (successorIndex < successors.Length)
                {
                    var successor = successors[successorIndex++];
                    if (!allowed.Contains(successor))
                    {
                        continue;
                    }
                    if (!indices.TryGetValue(successor, out var visitedIndex))
                    {
                        // Descend: this frame resumes after the successor, taking its low link then.
                        frames.Push((node, successorIndex));
                        Enter(successor);
                        descended = true;
                        break;
                    }
                    if (onStack.Contains(successor))
                    {
                        lowLinks[node] = Math.Min(lowLinks[node], visitedIndex);
                    }
                }
                if (descended)
                {
                    continue;
                }
                Leave(node);
                if (frames.TryPeek(out var parent))
                {
                    // The parent's descent into this node returns here.
                    lowLinks[parent.Node] = Math.Min(lowLinks[parent.Node], lowLinks[node]);
                }
            }
        }

        void Enter(int node)
        {
            indices[node] = nextIndex;
            lowLinks[node] = nextIndex;
            nextIndex++;
            stack.Push(node);
            onStack.Add(node);
            frames.Push((node, 0));
        }

        void Leave(int node)
        {
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

        return components.ToImmutable();
    }
}
