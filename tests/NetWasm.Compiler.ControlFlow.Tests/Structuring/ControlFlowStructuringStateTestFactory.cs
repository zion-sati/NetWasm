using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using System.Collections.Generic;
using System;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

internal static class ControlFlowStructuringStateTestFactory
{
    internal static ControlFlowStructuringState Create()
    {
        return new ControlFlowStructuringState(new ValidatedControlFlowGraph(new ControlFlowGraph(default!, Enumerable.Range(0, 103).Select(index => new BasicBlock(index, index, ImmutableArray<CilInstruction>.Empty)).ToImmutableArray(), ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(0, ImmutableArray.Create(11, 99)).Add(11, ImmutableArray<int>.Empty).Add(12, ImmutableArray<int>.Empty).Add(13, ImmutableArray<int>.Empty).Add(21, ImmutableArray<int>.Empty).Add(22, ImmutableArray<int>.Empty).Add(23, ImmutableArray<int>.Empty).Add(99, ImmutableArray<int>.Empty).Add(101, ImmutableArray<int>.Empty).Add(102, ImmutableArray<int>.Empty), ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(0, ImmutableArray<int>.Empty).Add(11, ImmutableArray<int>.Empty).Add(12, ImmutableArray<int>.Empty).Add(13, ImmutableArray<int>.Empty).Add(21, ImmutableArray<int>.Empty).Add(22, ImmutableArray<int>.Empty).Add(23, ImmutableArray<int>.Empty).Add(99, ImmutableArray<int>.Empty).Add(101, ImmutableArray<int>.Empty).Add(102, ImmutableArray<int>.Empty), ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(0, ImmutableArray<int>.Empty).Add(11, ImmutableArray<int>.Empty).Add(12, ImmutableArray<int>.Empty).Add(13, ImmutableArray<int>.Empty).Add(21, ImmutableArray<int>.Empty).Add(22, ImmutableArray<int>.Empty).Add(23, ImmutableArray<int>.Empty).Add(99, ImmutableArray<int>.Empty).Add(101, ImmutableArray<int>.Empty).Add(102, ImmutableArray<int>.Empty), ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(0, ImmutableArray<int>.Empty).Add(11, ImmutableArray<int>.Empty).Add(12, ImmutableArray<int>.Empty).Add(13, ImmutableArray<int>.Empty).Add(21, ImmutableArray<int>.Empty).Add(22, ImmutableArray<int>.Empty).Add(23, ImmutableArray<int>.Empty).Add(99, ImmutableArray<int>.Empty).Add(101, ImmutableArray<int>.Empty).Add(102, ImmutableArray<int>.Empty), ImmutableHashSet<int>.Empty), ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
    }

    internal static ControlFlowStructuringState CreateSingleReturnBlock()
    {
        var template = Create();
        var instruction = new CilInstruction(
            0,
            1,
            CilOperation.Return,
            new CilOperand.None());
        var graph = new ControlFlowGraph(
            template.Graph.MethodBody,
            ImmutableArray.Create(new BasicBlock(
                0,
                0,
                ImmutableArray.Create(instruction))),
            ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(
                0,
                ImmutableArray<int>.Empty),
            ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(
                0,
                ImmutableArray<int>.Empty),
            ImmutableDictionary<int, ImmutableArray<int>>.Empty,
            ImmutableDictionary<int, ImmutableArray<int>>.Empty,
            ImmutableHashSet.Create(0));
        var validated = new ValidatedControlFlowGraph(
            graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(
                0,
                ImmutableArray<CliValueKind>.Empty),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(
                0,
                ImmutableArray<CliValueKind>.Empty));

        return new ControlFlowStructuringState(validated);
    }
}
