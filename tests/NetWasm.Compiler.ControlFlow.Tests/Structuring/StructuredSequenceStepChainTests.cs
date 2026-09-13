using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class StructuredSequenceStepChainTests
{
    [Fact]
    public void ExecuteStopsAtTheFirstHandledStep()
    {
        var first = new SequenceStepProbe(NotHandled(11));
        var second = new SequenceStepProbe(ContinueAt(22));
        var third = new SequenceStepProbe(ContinueAt(33));
        var chain = new StructuredSequenceStepChain(
            ImmutableArray.Create<IStructuredSequenceStepExecutor>(first, second, third));

        var result = Execute(chain);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Equal(22, result.NextBlockOffset);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
        Assert.Equal(0, third.CallCount);
    }

    [Fact]
    public void ExecuteReturnsNotHandledWhenEveryStepDeclines()
    {
        var first = new SequenceStepProbe(NotHandled(11));
        var second = new SequenceStepProbe(NotHandled(11));
        var chain = new StructuredSequenceStepChain(
            ImmutableArray.Create<IStructuredSequenceStepExecutor>(first, second));

        var result = Execute(chain);

        Assert.Equal(StructuredSequenceStepDisposition.NotHandled, result.Disposition);
        Assert.Equal(11, result.NextBlockOffset);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    private static StructuredSequenceStepResult Execute(StructuredSequenceStepChain chain)
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var recursiveBuilder = new StructuredControlFlowBuilderProbe();
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();

        return ((IStructuredSequenceStepExecutor)chain).Execute(
            recursiveBuilder,
            state,
            11,
            null,
            ImmutableHashSet<int>.Empty,
            [],
            regions);
    }

    private static StructuredSequenceStepResult NotHandled(int cursor)
    {
        return new StructuredSequenceStepResult(
            StructuredSequenceStepDisposition.NotHandled,
            cursor);
    }

    private static StructuredSequenceStepResult ContinueAt(int cursor)
    {
        return new StructuredSequenceStepResult(
            StructuredSequenceStepDisposition.Continue,
            cursor);
    }

    private sealed class SequenceStepProbe(
        StructuredSequenceStepResult result) : IStructuredSequenceStepExecutor
    {
        internal int CallCount { get; private set; }

        public StructuredSequenceStepResult Execute(
            IStructuredControlFlowBuilder structuredControlFlowBuilder,
            ControlFlowStructuringState state,
            int currentBlockOffset,
            int? stop,
            ImmutableHashSet<int> allowed,
            HashSet<int> path,
            ImmutableArray<StructuredRegionDraft>.Builder regions)
        {
            CallCount++;
            return result;
        }
    }
}
