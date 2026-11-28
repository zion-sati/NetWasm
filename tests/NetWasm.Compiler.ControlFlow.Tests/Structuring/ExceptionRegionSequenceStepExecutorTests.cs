// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ExceptionRegionSequenceStepExecutorTests
{
    [Fact]
    public void ExecuteDeclinesAnOffsetOutsideExceptionAndDispatcherRegions()
    {
        var distances = new ControlFlowDistanceFinder();
        var joins = new CommonReachableBlockFinder(
            new ControlFlowPostDominatorFinder(new ControlFlowPostDominanceAnalyzer(new DominatorTreeBuilder())),
            distances);
        var reachableBlocks = new ReachableBlockFinder(distances);
        var command = new ExceptionRegionSequenceStepExecutor(
            new ReachableSetOverlapClassifier(),
            new DispatcherBoundaryClipper(),
            joins,
            reachableBlocks,
            new DispatcherFallthroughExitClassifier());
        var state = ControlFlowStructuringStateTestFactory.Create();
        var recursiveBuilder = new StructuredControlFlowBuilderProbe();
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        const int cursor = int.MaxValue;

        var result = ((IStructuredSequenceStepExecutor)command).Execute(
            recursiveBuilder,
            state,
            cursor,
            null,
            ImmutableHashSet<int>.Empty,
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.NotHandled, result.Disposition);
        Assert.Equal(cursor, result.NextBlockOffset);
        Assert.Empty(regions);
        Assert.Equal(0, recursiveBuilder.CallCount);
    }
}
