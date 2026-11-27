// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class CallArgumentConstantCollectorTests
{
    [Fact]
    public void RejectsAStackSnapshotShorterThanTheResolvedCallSignature()
    {
        var caller = Method();
        var body = Body(Instruction(0, CilOperation.Call, new CilOperand.MethodInstance(Method(2))),
            Instruction(1, CilOperation.Return)) with
        { Method = caller.Definition, MethodInstance = caller };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var entries = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, [CliValueKind.ManagedReference]);
        var managed = new ManagedMethodBody(caller, new(graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty, entries));

        Assert.Throws<InvalidOperationException>(() =>
            AsCollector(new CallArgumentConstantCollector(new MethodFacts())).Collect(managed));
    }

    [Fact]
    public void RejectsMissingResolverAndBody()
    {
        Assert.Throws<ArgumentNullException>(() => new CallArgumentConstantCollector(null!));
        Assert.Throws<ArgumentNullException>(() => AsCollector(new CallArgumentConstantCollector(new MethodFacts())).Collect(null!));
    }

    [Fact]
    public void PreservesConstantsThroughDupAndPopWithoutInferringUnreachableCalls()
    {
        var target = Method(2);
        var caller = Method();
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.Duplicate),
            Instruction(2, CilOperation.Pop),
            Instruction(3, CilOperation.LoadNull),
            Instruction(4, CilOperation.Call, new CilOperand.MethodInstance(target)),
            Instruction(5, CilOperation.Return),
            Instruction(6, CilOperation.Call, new CilOperand.MethodInstance(target)),
            Instruction(7, CilOperation.Return)) with
        { Method = caller.Definition, MethodInstance = caller };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        int[] depths = [0, 1, 2, 1, 2, 1];
        var stacks = depths.Select((count, offset) => KeyValuePair.Create(offset,
            Enumerable.Repeat(CliValueKind.ManagedReference, count).ToImmutableArray())).ToImmutableDictionary();
        var managed = new ManagedMethodBody(caller, new(graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty, stacks));

        var call = Assert.Single(AsCollector(new CallArgumentConstantCollector(new MethodFacts())).Collect(managed));

        Assert.Same(target, call.Method);
        Assert.Equal(2, call.Arguments.Length);
        Assert.All(call.Arguments, value => Assert.IsType<ScalarConstant.Null>(value));
    }

    [Fact]
    public void RetainsLiteralArgumentsBetweenUnknownValueAndProvider()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4,
            CliValueKind.I4, CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.ManagedReference);
        var target = Method(2) with { Signature = signature };
        target = target with { Definition = target.Definition with { Signature = signature } };
        var caller = Method();
        var callerSignature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4, CliValueKind.ManagedReference);
        caller = caller with { Definition = caller.Definition with { Signature = callerSignature }, Signature = callerSignature };
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Instruction(2, CilOperation.LoadNull),
            Instruction(3, CilOperation.LoadArgument, new CilOperand.Index(1)),
            Instruction(4, CilOperation.Call, new CilOperand.MethodInstance(target)),
            Instruction(5, CilOperation.Return)) with
        { Method = caller.Definition, MethodInstance = caller };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        int[] depths = [0, 1, 2, 3, 4, 1];
        var stacks = depths.Select((count, offset) =>
            KeyValuePair.Create(offset, Enumerable.Repeat(CliValueKind.I4, count).ToImmutableArray())).ToImmutableDictionary();
        var method = new ManagedMethodBody(caller, new(graph, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty, stacks));
        var collector = AsCollector(new CallArgumentConstantCollector(new MethodFacts()));

        var call = Assert.Single(collector.Collect(method));

        Assert.Same(target, call.Method);
        Assert.Collection(call.Arguments,
            Assert.Null,
            value => Assert.Equal(new ScalarConstant.Int32(0), value),
            value => Assert.IsType<ScalarConstant.Null>(value),
            Assert.Null);
        // The caller's own parameters are passed on unchanged, so the call says which.
        Assert.Equal([0, -1, -1, 1], call.ForwardedParameters);
    }

    // A parameter the caller stores to, or takes the address of, may not hold its caller's value, so passing it on
    // forwards nothing; a call that forwards nothing leaves ForwardedParameters default.
    [Fact]
    public void AParameterTheCallerChangesIsNotForwarded()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var target = Method(2) with { Signature = signature };
        target = target with { Definition = target.Definition with { Signature = signature } };
        var caller = Method();
        caller = caller with { Definition = caller.Definition with { Signature = signature }, Signature = signature };
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            Instruction(1, CilOperation.StoreArgument, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(3, CilOperation.Call, new CilOperand.MethodInstance(target)),
            Instruction(4, CilOperation.Return)) with
        { Method = caller.Definition, MethodInstance = caller };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        int[] depths = [0, 1, 0, 1, 1];
        var stacks = depths.Select((count, offset) =>
            KeyValuePair.Create(offset, Enumerable.Repeat(CliValueKind.I4, count).ToImmutableArray())).ToImmutableDictionary();
        var method = new ManagedMethodBody(caller, new(graph, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty, stacks));

        var call = Assert.Single(AsCollector(new CallArgumentConstantCollector(new MethodFacts())).Collect(method));

        Assert.True(call.ForwardedParameters.IsDefault);
    }

    private static ICallArgumentConstantCollector AsCollector(ICallArgumentConstantCollector collector) => collector;
}
