using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class DispatcherSequenceStepExecutorTests
{
    [Fact]
    public void ExecuteReturnsTheUnchangedCursorWhenNoDispatcherMatches()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var command = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(
            new DispatcherSequenceStepExecutor(
            new ActiveControlFlowBlockClipper(),
            new EmptyLoopActiveBlockRetentionSelector()));
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

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var clipper = new ActiveControlFlowBlockClipper();
        var retention = new EmptyLoopActiveBlockRetentionSelector();

        Assert.Throws<ArgumentNullException>(() => new DispatcherSequenceStepExecutor(
            null!,
            retention));
        Assert.Throws<ArgumentNullException>(() => new DispatcherSequenceStepExecutor(
            clipper,
            null!));
    }

    private sealed class EmptyLoopActiveBlockRetentionSelector : ILoopActiveBlockRetentionSelector
    {
        public ImmutableHashSet<int> Select(
            ControlFlowStructuringState state,
            LoopRegion loop)
        {
            return ImmutableHashSet<int>.Empty;
        }
    }

}
