// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ScalarCallLifetimePlannerTests
{
    [Theory]
    [InlineData(CliValueKind.F4)]
    [InlineData(CliValueKind.F8)]
    [InlineData(CliValueKind.I8)]
    public void PreservesLoopProducedLocalAcrossLaterCallIncludingZeroTripPath(CliValueKind kind)
    {
        var fixture = Create(kind);
        var planner = new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops);

        var plan = planner.Plan(fixture.Method, true);

        Assert.Equal([0], plan.CallLocals[10].ToArray());
        Assert.Single(plan.CallLocals);
        Assert.Equal(1, fixture.Graphs.Calls);
        Assert.Equal(1, fixture.Loops.Calls);
        Assert.Same(fixture.Method.Header.Method, fixture.Graphs.Observed!.Method);
        Assert.Equal(fixture.Method.Header.LocalSignatureTypes, fixture.Graphs.Observed.LocalSignatureTypes);
    }

    [Fact]
    public void OutsideAssignmentKillsLoopDefinitionBeforeCall()
    {
        var fixture = Create(CliValueKind.F8,
            [(CilOperation.LoadFloat64, new CilOperand.None()),
             (CilOperation.StoreLocal, new CilOperand.Index(0))]);

        Assert.Empty(new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops)
            .Plan(fixture.Method, true).CallLocals);
    }

    [Fact]
    public void DeadLocalAfterCallDoesNotNeedPreservation()
    {
        var fixture = Create(CliValueKind.F8, readAfterCall: false);

        Assert.Empty(new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops)
            .Plan(fixture.Method, true).CallLocals);
    }

    [Fact]
    public void AddressExposedLocalIsExcludedBeforeAnalysis()
    {
        var fixture = Create(CliValueKind.F8,
            [(CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
             (CilOperation.Pop, new CilOperand.None())]);

        Assert.Empty(new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops)
            .Plan(fixture.Method, true).CallLocals);
        Assert.Equal(0, fixture.Graphs.Calls);
    }

    [Fact]
    public void Int32LocalIsNotSelected()
    {
        var fixture = Create(CliValueKind.I4);

        Assert.Empty(new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops)
            .Plan(fixture.Method, true).CallLocals);
        Assert.Equal(0, fixture.Graphs.Calls);
    }

    [Fact]
    public void DisabledPolicyDoesNotAnalyzeBody()
    {
        var fixture = Create(CliValueKind.F8);

        Assert.Same(ScalarCallLifetimePlan.Empty,
            new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops).Plan(fixture.Method, false));
        Assert.Equal(0, fixture.Graphs.Calls);
        Assert.Equal(0, fixture.Loops.Calls);
    }

    [Fact]
    public void ExceptionRegionsAreExcludedBeforeAnalysis()
    {
        var fixture = Create(CliValueKind.F8);
        var group = new StructuredExceptionGroupId(0);
        var method = fixture.Method with
        {
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty
                .Add(group, new(group, null, 0, 10, [], [], [], null, null)),
        };

        Assert.Empty(new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops)
            .Plan(method, true).CallLocals);
        Assert.Equal(0, fixture.Graphs.Calls);
    }

    [Fact]
    public void NoNaturalLoopDoesNotSelectCalls()
    {
        var fixture = Create(CliValueKind.F8);
        fixture.Loops.Result = [];

        Assert.Empty(new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops)
            .Plan(fixture.Method, true).CallLocals);
    }

    [Fact]
    public void CallWithinLoopIsNotSelected()
    {
        var fixture = Create(CliValueKind.F8, callInsideLoop: true);

        var plan = new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops).Plan(fixture.Method, true);

        Assert.Single(plan.CallLocals);
        Assert.Contains(10, plan.CallLocals.Keys);
        Assert.DoesNotContain(8, plan.CallLocals.Keys);
    }

    [Fact]
    public void RepeatedLaterCallsEachPreserveLiveValue()
    {
        var fixture = Create(CliValueKind.F8,
            [(CilOperation.Call, new CilOperand.None())]);

        var plan = new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops).Plan(fixture.Method, true);

        Assert.Equal([10, 11], plan.CallLocals.Keys.Order().ToArray());
        Assert.All(plan.CallLocals.Values, locals => Assert.Equal([0], locals.ToArray()));
    }

    [Fact]
    public void NullContractsFailBeforeAnalysis()
    {
        var fixture = Create(CliValueKind.F8);
        Assert.Throws<ArgumentNullException>(() => new ScalarCallLifetimePlanner(null!, fixture.Loops));
        Assert.Throws<ArgumentNullException>(() => new ScalarCallLifetimePlanner(fixture.Graphs, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ScalarCallLifetimePlanner(fixture.Graphs, fixture.Loops).Plan(null!, true));
        Assert.Equal(0, fixture.Graphs.Calls);
    }

    private static Fixture Create(
        CliValueKind kind,
        (CilOperation Operation, CilOperand Operand)[]? beforeCall = null,
        bool readAfterCall = true,
        bool callInsideLoop = false)
    {
        var constant = kind switch
        {
            CliValueKind.F4 => CilOperation.LoadFloat32,
            CliValueKind.F8 => CilOperation.LoadFloat64,
            _ => CilOperation.LoadInt64,
        };
        var operations = new List<(CilOperation Operation, CilOperand Operand)>
        {
            (constant, new CilOperand.None()),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),
            (CilOperation.LoadArgument, new CilOperand.Index(0)),
            (CilOperation.BranchIfFalse, new CilOperand.BranchTarget(10)),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (constant, new CilOperand.None()),
            (CilOperation.Add, new CilOperand.None()),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),
            (callInsideLoop ? CilOperation.Call : CilOperation.LoadArgument,
                callInsideLoop ? new CilOperand.None() : new CilOperand.Index(0)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
        };
        operations.AddRange(beforeCall ?? []);
        operations.Add((CilOperation.Call, new CilOperand.None()));
        operations.Add(readAfterCall
            ? (CilOperation.LoadLocal, new CilOperand.Index(0))
            : (constant, new CilOperand.None()));
        operations.Add((CilOperation.Return, new CilOperand.None()));
        var instructions = operations.Select((operation, offset) =>
            new CilInstruction(offset, offset + 1, operation.Operation, operation.Operand)).ToImmutableArray();
        var assembly = new AssemblyIdentity("floating-lifetime-tests");
        var definition = new MethodDefinitionModel(new(assembly, 0x06000001), new(assembly, 0x02000001),
            "Body", true, MethodSignatureModel.Create(kind, CliValueKind.I4), 1);
        var body = new CilMethodBody(definition, 2, [kind], instructions);
        // Fixture construction only. The planner's collaborators below return
        // frozen in-memory facts, so these tests exercise selection independently.
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var method = new StructuredMethod(new(definition, null, 2, [kind], body.LocalSignatureTypes, instructions),
            new(0), ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty,
            StructuredSequence.Empty, [], ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);
        return new(method, new(graph), new());
    }

    private sealed record Fixture(StructuredMethod Method, Graphs Graphs, Loops Loops);

    private sealed class Graphs(ControlFlowGraph graph) : IControlFlowGraphBuilder
    {
        public int Calls { get; private set; }
        public CilMethodBody? Observed { get; private set; }
        public ControlFlowGraph Build(CilMethodBody body)
        {
            Calls++;
            Observed = body;
            return graph;
        }
    }

    private sealed class Loops : IControlFlowNaturalLoopAnalyzer
    {
        public int Calls { get; private set; }
        public ImmutableArray<ImmutableHashSet<int>> Result { get; set; } = [ImmutableHashSet.Create(1)];
        public ImmutableArray<ImmutableHashSet<int>> Analyze(ControlFlowGraph graph, int entry, ImmutableHashSet<int> blocks)
        {
            Calls++;
            Assert.Equal(0, entry);
            Assert.Equal([0, 1, 2], blocks.Order().ToArray());
            return Result;
        }
    }
}
