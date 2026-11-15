// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.GarbageCollection.Tests;

public sealed class StackStorageContinuationAnalyzerTests
{
    private static readonly AssemblyIdentity Assembly = new("StorageContinuations");
    private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
    private static readonly EntityKey MethodKey = new(Assembly, 0x06000001);
    private static readonly EntityKey CallKey = new(Assembly, 0x06000002);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);

    [Fact]
    public void OrdinaryBranchesPreserveTheStackAndDoNotInventExceptionalDestinations()
    {
        var method = Body([I(0, CilOperation.Branch, 2), I(1, CilOperation.Return), I(2, CilOperation.Return)]);
        var flow = Analyze(method);
        AssertEdges(method, flow.Normal, 0, false, 2);
        Assert.All(flow.Exceptional.Values, edges => Assert.Empty(edges));
        Assert.Empty(flow.Normal[Block(method, 2)]);
    }

    [Fact]
    public void SiblingFiltersSearchInMetadataOrderAndThrowingFiltersOnlyReject()
    {
        var method = Body(
            [I(0, CilOperation.LoadNull), I(1, CilOperation.Throw),
             I(2, CilOperation.Pop), Call(3), Constant(4), I(5, CilOperation.EndFilter),
             I(6, CilOperation.Pop), I(7, CilOperation.Leave, 14),
             I(8, CilOperation.Pop), Call(9), Constant(10), I(11, CilOperation.EndFilter),
             I(12, CilOperation.Pop), I(13, CilOperation.Leave, 14), I(14, CilOperation.Return)],
            [Filter(0, 2, 2, 6, 2), Filter(0, 2, 8, 12, 2)]);
        var flow = Analyze(method);
        AssertEdges(method, flow.Exceptional, 0, true, 2);
        AssertEdges(method, flow.Normal, 2, true, 6, 8);
        AssertEdges(method, flow.Exceptional, 2, true, 8);
        AssertEdges(method, flow.Normal, 8, true, 12);
        Assert.Empty(flow.Exceptional[Block(method, 8)]);
    }

    [Fact]
    public void OuterFilterRunsBeforeTheOriginalThrowingOriginsInnerCleanup()
    {
        var method = Body(
            [I(0, CilOperation.LoadNull), I(1, CilOperation.Throw),
             Call(2), I(3, CilOperation.EndFinally),
             I(4, CilOperation.Pop), Call(5), Constant(6), I(7, CilOperation.EndFilter),
             I(8, CilOperation.Pop), I(9, CilOperation.Leave, 10), I(10, CilOperation.Return)],
            [Cleanup(CilExceptionRegionKind.Finally, 0, 2, 2, 2), Filter(0, 4, 4, 8, 2)]);
        var flow = Analyze(method);
        AssertEdges(method, flow.Exceptional, 0, true, 4);
        // The same filter also handles new exceptions from inside the cleanup.
        // Those origins must not reenter the cleanup that is already running.
        AssertEdges(method, flow.Normal, 4, true, 2, 8);
        AssertEdges(method, flow.Exceptional, 4, true, 2);
        AssertEdges(method, flow.Normal, 2, true, 8);
        AssertEdges(method, flow.Exceptional, 2, true, 4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NormalLeaveRunsOnlyTheCleanupsWhoseProtectedScopesItExits(bool staysInOuterTry)
    {
        var method = Body(
            [I(0, CilOperation.Nop), I(1, CilOperation.Leave, staysInOuterTry ? 4 : 8),
             I(2, CilOperation.Nop), I(3, CilOperation.EndFinally),
             I(4, CilOperation.Nop), I(5, CilOperation.Leave, 8),
             I(6, CilOperation.Nop), I(7, CilOperation.EndFinally), I(8, CilOperation.Return)],
            [Cleanup(CilExceptionRegionKind.Finally, 0, 2, 2, 2),
             Cleanup(CilExceptionRegionKind.Finally, 0, 6, 6, 2)]);
        var flow = Analyze(method);
        AssertEdges(method, flow.Normal, 0, true, 2);
        AssertEdges(method, flow.Normal, 2, true, staysInOuterTry ? 4 : 6);
        AssertEdges(method, flow.Normal, 6, true, 8);
        if (staysInOuterTry) AssertEdges(method, flow.Normal, 4, true, 6);
    }

    [Theory]
    [InlineData(CilExceptionRegionKind.Fault)]
    [InlineData(CilExceptionRegionKind.Finally)]
    public void FaultRunsOnlyOnUnwindWhileFinallyAlsoRunsOnNormalLeave(CilExceptionRegionKind kind)
    {
        var method = Body(
            [Call(0), I(1, CilOperation.Leave, 4), I(2, CilOperation.Nop),
             I(3, CilOperation.EndFinally), I(4, CilOperation.Return)],
            [Cleanup(kind, 0, 2, 2, 2)]);
        var flow = Analyze(method);
        AssertEdges(method, flow.Exceptional, 0, true, 2);
        AssertEdges(method, flow.Normal, 0, true, kind == CilExceptionRegionKind.Fault ? 4 : 2);
        if (kind == CilExceptionRegionKind.Fault) Assert.Empty(flow.Normal[Block(method, 2)]);
        else AssertEdges(method, flow.Normal, 2, true, 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExceptionFromACatchOrCleanupSearchesOuterClausesWithoutReenteringItself(bool cleanup)
    {
        var inner = cleanup ? Cleanup(CilExceptionRegionKind.Finally, 0, 2, 2, 2) : Catch(0, 2, 2, 2);
        var method = Body(
            [I(0, CilOperation.LoadNull), I(1, CilOperation.Throw),
             I(2, cleanup ? CilOperation.LoadNull : CilOperation.Pop),
             I(3, cleanup ? CilOperation.Throw : CilOperation.Rethrow),
             I(4, CilOperation.Pop), I(5, CilOperation.Leave, 6), I(6, CilOperation.Return)],
            [inner, Catch(0, 4, 4, 2)]);
        AssertEdges(method, Analyze(method).Exceptional, 2, true, 4);
    }

    [Fact]
    public void SharedFinallyUnionsTwoNormalTargetsAndTheSelectedUnwindHandler()
    {
        var method = Body(
            [Constant(0), I(1, CilOperation.BranchIfTrue, 4), I(2, CilOperation.Nop),
             I(3, CilOperation.Leave, 8), Call(4), I(5, CilOperation.Leave, 9),
             I(6, CilOperation.Nop), I(7, CilOperation.EndFinally),
             I(8, CilOperation.Leave, 13), I(9, CilOperation.Leave, 13),
             I(10, CilOperation.Pop), I(11, CilOperation.Nop), I(12, CilOperation.Leave, 13),
             I(13, CilOperation.Return)],
            [Cleanup(CilExceptionRegionKind.Finally, 0, 6, 6, 2), Catch(0, 10, 10, 3)]);
        var flow = Analyze(method);
        AssertEdges(method, flow.Normal, 6, true, 8, 9, 10);
        AssertEdges(method, flow.Exceptional, 4, true, 6);
    }

    [Fact]
    public void AnUnreachableLeaveCannotRegisterItsTargetOnAReachableFinally()
    {
        var method = Body(
            [I(0, CilOperation.Branch, 2), I(1, CilOperation.Leave, 7),
             I(2, CilOperation.Nop), I(3, CilOperation.Leave, 6),
             I(4, CilOperation.Nop), I(5, CilOperation.EndFinally),
             I(6, CilOperation.Return), I(7, CilOperation.Return)],
            [Cleanup(CilExceptionRegionKind.Finally, 0, 4, 4, 2)]);
        Assert.DoesNotContain(Block(method, 1), method.ControlFlow.Graph.ReachableBlocks);
        Assert.DoesNotContain(Block(method, 7), method.ControlFlow.Graph.ReachableBlocks);
        AssertEdges(method, Analyze(method).Normal, 4, true, 6);
    }

    [Fact]
    public void MissingMethodIsRejectedAtTheCapabilityBoundary()
    {
        var analyzer = new IStackStorageContinuationAnalyzer[] { new StackStorageContinuationAnalyzer() }[0];
        Assert.Throws<ArgumentNullException>(() => analyzer.Analyze(null!));
    }

    [Fact]
    public void NestedFinallyReturnsToItsOwnContinuationBeforeTheOuterFinallyCompletes()
    {
        var method = Body(
            [Call(0), I(1, CilOperation.Leave, 9),
             I(2, CilOperation.Nop), I(3, CilOperation.Leave, 6),
             I(4, CilOperation.Nop), I(5, CilOperation.EndFinally),
             I(6, CilOperation.Nop), I(7, CilOperation.Nop), I(8, CilOperation.EndFinally),
             I(9, CilOperation.Return)],
            [Cleanup(CilExceptionRegionKind.Finally, 0, 2, 2, 7),
             Cleanup(CilExceptionRegionKind.Finally, 2, 2, 4, 2)]);

        var flow = Analyze(method);
        AssertEdges(method, flow.Normal, 0, true, 2);
        AssertEdges(method, flow.Normal, 2, true, 4);
        AssertEdges(method, flow.Normal, 4, true, 6);
        AssertEdges(method, flow.Normal, 6, true, 9);
        AssertEdges(method, flow.Exceptional, 0, true, 2);
    }

    [Fact]
    public void LeavingNestedCatchRunsTheEnclosingFinally()
    {
        var method = Body(
            [I(0, CilOperation.LoadNull), I(1, CilOperation.Throw),
             I(2, CilOperation.Pop), I(3, CilOperation.Leave, 6),
             I(4, CilOperation.Nop), I(5, CilOperation.EndFinally), I(6, CilOperation.Return)],
            [Catch(0, 2, 2, 2), Cleanup(CilExceptionRegionKind.Finally, 0, 4, 4, 2)]);

        var flow = Analyze(method);
        AssertEdges(method, flow.Exceptional, 0, true, 2, 4);
        AssertEdges(method, flow.Normal, 2, true, 4);
        AssertEdges(method, flow.Normal, 4, true, 6);
    }

    [Fact]
    public void NestedFilterSelectsItsHandlerBeforeTheEnclosingFinally()
    {
        var method = Body(
            [I(0, CilOperation.LoadNull), I(1, CilOperation.Throw),
             I(2, CilOperation.Pop), Constant(3), I(4, CilOperation.EndFilter),
             I(5, CilOperation.Pop), I(6, CilOperation.Leave, 9),
             I(7, CilOperation.Nop), I(8, CilOperation.EndFinally), I(9, CilOperation.Return)],
            [Filter(0, 2, 2, 5, 2), Cleanup(CilExceptionRegionKind.Finally, 0, 7, 7, 2)]);

        var flow = Analyze(method);
        AssertEdges(method, flow.Exceptional, 0, true, 2);
        AssertEdges(method, flow.Normal, 2, true, 5, 7);
        AssertEdges(method, flow.Normal, 5, true, 7);
        AssertEdges(method, flow.Normal, 7, true, 9);
    }

    private static StackStorageContinuations Analyze(ManagedMethodBody method) =>
        ((IStackStorageContinuationAnalyzer)new StackStorageContinuationAnalyzer()).Analyze(method);

    private static int Block(ManagedMethodBody method, int offset) =>
        method.ControlFlow.Graph.GetBlockAtOffset(offset).Index;

    private static void AssertEdges(ManagedMethodBody method,
        ImmutableDictionary<int, ImmutableArray<StackStorageFlowEdge>> edges,
        int origin, bool reset, params int[] targets) =>
        Assert.Equal(targets.Select(offset => new StackStorageFlowEdge(Block(method, offset), reset))
            .OrderBy(edge => edge.Block).ToArray(), edges[Block(method, origin)].ToArray());

    private static CilExceptionRegion Cleanup(CilExceptionRegionKind kind, int start, int length, int handler, int handlerLength) =>
        new(kind, start, length, handler, handlerLength, null, null);

    private static CilExceptionRegion Filter(int start, int length, int filter, int handler, int handlerLength) =>
        new(CilExceptionRegionKind.Filter, start, length, handler, handlerLength, null, filter);

    private static CilExceptionRegion Catch(int start, int length, int handler, int handlerLength) =>
        new(CilExceptionRegionKind.Catch, start, length, handler, handlerLength, TypeKey, null);

    private static CilInstruction I(int offset, CilOperation operation, int? target = null) =>
        new(offset, offset + 1, operation, target is { } value ? new CilOperand.BranchTarget(value) : new CilOperand.None());

    private static CilInstruction Call(int offset) => new(offset, offset + 1, CilOperation.Call, new CilOperand.Entity(CallKey));

    private static CilInstruction Constant(int offset) => new(offset, offset + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1));

    private static ManagedMethodBody Body(ImmutableArray<CilInstruction> instructions, ImmutableArray<CilExceptionRegion> regions = default)
    {
        var definition = new MethodDefinitionModel(MethodKey, TypeKey, "Entry", true, new(Void, []), 1);
        var type = CliTypeIdentity.Named(Assembly, "Tests", "Exception", false);
        var method = new MethodInstanceModel(definition, type, [], definition.Signature);
        var body = new CilMethodBody(definition, 2, [], instructions)
        {
            MethodInstance = method,
            ExceptionRegions = regions.IsDefault ? [] : regions,
        };
        var graph = ((IControlFlowGraphBuilderFactory)new ControlFlowGraphBuilderFactory()).Create().Build(body);
        var repository = new Repository();
        var validated = new TypedStackValidator(repository, repository, repository, new StackTypeCompatibilityValidator()).Validate(graph);
        return new(method, validated);
    }

    private sealed class Repository : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(key, "Tests", "Exception", false, [], []);

        public FieldDefinitionModel GetField(EntityKey key) => throw new InvalidOperationException();

        public MethodDefinitionModel GetMethod(EntityKey key) => new(key, TypeKey, "Call", true, new(Void, []), 1);
    }
}
