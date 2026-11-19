// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests;

// The analyzer walks a method's blocks with an explicit stack: a path through a method is as long as the method,
// and the recursive walk it replaces overflowed a thread's stack on a 20,000-arm if/else-if chain.
public sealed class ControlFlowComponentAnalyzerTests
{
    // The components and their order are the recursive Tarjan algorithm's, over many shapes of loops and branches.
    [Fact]
    public void ComponentsMatchTheRecursiveAlgorithmInContentAndOrder()
    {
        var random = new Random(20261119);
        for (var round = 0; round < 300; round++)
        {
            var graph = ControlFlowGraphBuilder.Build(RandomBody(random, random.Next(2, 40)));
            var allowed = random.Next(4) == 0
                ? graph.ReachableBlocks.Where(_ => random.Next(5) != 0).ToImmutableHashSet()
                : graph.ReachableBlocks;

            var components = new ControlFlowComponentAnalyzer().Analyze(graph, allowed);

            var expected = RecursiveComponents(graph, allowed);
            Assert.Equal(expected.Length, components.Length);
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.True(expected[index].SetEquals(components[index]), $"round {round}, component {index}");
            }
        }
    }

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
        ImmutableArray<ImmutableHashSet<int>> components = default;

        // A quarter-megabyte stack, as a small thread has: the recursive walk needed a frame per block.
        var thread = new Thread(() => components = new ControlFlowComponentAnalyzer().Analyze(graph, graph.ReachableBlocks),
            256 * 1024);
        thread.Start();
        thread.Join();

        Assert.Equal(graph.ReachableBlocks.Count, components.Length);
        Assert.All(components, component => Assert.Single(component));
    }

    [Fact]
    public void ItRejectsAMissingGraphOrBlockSet()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowComponentAnalyzer().Analyze(null!, graph.ReachableBlocks));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowComponentAnalyzer().Analyze(graph, null!));
    }

    // Straight runs, conditional branches forward and back, and unconditional jumps, ending in a return.
    private static CilMethodBody RandomBody(Random random, int length)
    {
        var instructions = new List<CilInstruction>();
        var targets = new List<int>();
        for (var index = 0; index < length; index++)
        {
            var offset = instructions.Count;
            targets.Add(offset);
            switch (random.Next(4))
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
                default:
                    instructions.Add(I(offset, CilOperation.LoadArgument, new CilOperand.Index(0)));
                    instructions.Add(I(offset + 1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(-1)));
                    break;
            }
        }
        var end = instructions.Count;
        targets.Add(end);
        instructions.Add(I(end, CilOperation.Return));
        // Each branch goes to the start of a random statement, the return included.
        return Body(CliValueKind.Void, 1, [], [.. instructions.Select(instruction =>
            instruction.Operand is CilOperand.BranchTarget
                ? instruction with { Operand = new CilOperand.BranchTarget(targets[random.Next(targets.Count)]) }
                : instruction)]);
    }

    // The recursive algorithm the analyzer replaced, as the reference for its results and their order.
    private static ImmutableArray<ImmutableHashSet<int>> RecursiveComponents(ControlFlowGraph graph, ImmutableHashSet<int> allowed)
    {
        var nextIndex = 0;
        var indices = new Dictionary<int, int>();
        var lowLinks = new Dictionary<int, int>();
        var stack = new Stack<int>();
        var onStack = new HashSet<int>();
        var components = ImmutableArray.CreateBuilder<ImmutableHashSet<int>>();
        void Visit(int node)
        {
            indices[node] = lowLinks[node] = nextIndex++;
            stack.Push(node);
            onStack.Add(node);
            foreach (var successor in graph.Successors[node])
            {
                if (!allowed.Contains(successor)) continue;
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
            if (lowLinks[node] != indices[node]) return;
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
            if (!indices.ContainsKey(node)) Visit(node);
        }
        return components.ToImmutable();
    }
}
