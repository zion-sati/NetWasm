// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The analyzer's producers: the instruction that pushed each stack value, kept across a join where every path
// agrees and unknown where they disagree, and whether one consumer alone reads what a producer pushed.
public sealed class StackSlotOriginAnalyzerTests
{
    private static readonly AssemblyIdentity Assembly = new("Origins");
    private static readonly CliTypeIdentity Object = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);

    [Fact]
    public void RejectsAMissingGraph() =>
        Assert.Throws<ArgumentNullException>(() => new StackSlotOriginAnalyzer(new Calls([])).Analyze(null!));

    // A format pushed before an argument written with ?: reaches the call from both paths, so it keeps its producer
    // and the call alone reads it; the argument's two producers disagree, and each is read where they meet.
    [Fact]
    public void AValueBelowAJoinKeepsItsProducer()
    {
        var origins = Analyze(
            [6],
            (CilOperation.LoadString, new CilOperand.UserString("{0}"), 0),
            (CilOperation.LoadArgument, new CilOperand.Index(0), 1),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5), 2),
            (CilOperation.LoadNull, new CilOperand.None(), 1),
            (CilOperation.Branch, new CilOperand.BranchTarget(6), 2),
            (CilOperation.LoadString, new CilOperand.UserString("x"), 1),
            (CilOperation.Call, new CilOperand.None(), 2),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.Return, new CilOperand.None(), 0));

        Assert.Equal(0, origins.Producer(6, 0));
        Assert.Null(origins.Producer(6, 1));
        Assert.True(origins.IsOnlyReadBy(0, 6));
        Assert.False(origins.IsOnlyReadBy(3, 6));
        Assert.False(origins.IsOnlyReadBy(5, 6));
        Assert.Equal(6, origins.Producer(7, 0));
    }

    // A dup reads the value it copies and produces both copies; a pop reads what it pops.
    [Fact]
    public void ADuplicateReadsWhatItCopies()
    {
        var origins = Analyze(
            [3],
            (CilOperation.LoadString, new CilOperand.UserString("{0}"), 0),
            (CilOperation.Duplicate, new CilOperand.None(), 1),
            (CilOperation.Pop, new CilOperand.None(), 2),
            (CilOperation.Call, new CilOperand.None(), 1),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.Return, new CilOperand.None(), 0));

        Assert.Equal(1, origins.Producer(3, 0));
        Assert.False(origins.IsOnlyReadBy(0, 3));
        Assert.False(origins.IsOnlyReadBy(1, 3));
    }

    // A path that throws with a value still on the stack reads it there, so the value has another reader than the
    // call the other path reaches.
    [Fact]
    public void AThrowReadsWhatItLeavesOnTheStack()
    {
        var origins = Analyze(
            [5],
            (CilOperation.LoadString, new CilOperand.UserString("{0}"), 0),
            (CilOperation.LoadArgument, new CilOperand.Index(0), 1),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5), 2),
            (CilOperation.LoadNull, new CilOperand.None(), 1),
            (CilOperation.Throw, new CilOperand.None(), 2),
            (CilOperation.Call, new CilOperand.None(), 1),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.Return, new CilOperand.None(), 0));

        Assert.Equal(0, origins.Producer(5, 0));
        Assert.False(origins.IsOnlyReadBy(0, 5));
    }

    // Each call at the given offsets takes all the values its entry stack holds and returns one.
    private static StackSlotOrigins Analyze(
        int[] calls, params (CilOperation Operation, CilOperand Operand, int Height)[] instructions)
    {
        var body = new CilMethodBody(
            Method("Run", 1).Definition,
            3,
            [],
            [.. instructions.Select((instruction, offset) =>
                new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))]);
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var validated = new ValidatedControlFlowGraph(
            graph,
            graph.Blocks.ToImmutableDictionary(block => block.Index, block => Stack(instructions[block.StartOffset].Height)),
            body.Instructions.ToImmutableDictionary(instruction => instruction.Offset,
                instruction => Stack(instructions[instruction.Offset].Height)));
        return new StackSlotOriginAnalyzer(new Calls(calls.ToDictionary(
            offset => offset, offset => Method("Format", instructions[offset].Height)))).Analyze(validated);

        static ImmutableArray<CliValueKind> Stack(int height) =>
            [.. Enumerable.Repeat(CliValueKind.ManagedReference, height)];
    }

    private static MethodInstanceModel Method(string name, int parameters)
    {
        var signature = new MethodSignatureModel(Object, [.. Enumerable.Repeat(Object, parameters)]);
        return new(
            new MethodDefinitionModel(new EntityKey(Assembly, 0x06000001), new EntityKey(Assembly, 2), name, true, signature, 0),
            Object,
            [],
            signature);
    }

    private sealed class Calls(Dictionary<int, MethodInstanceModel> calls) : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => calls.GetValueOrDefault(instruction.Offset);
    }
}
