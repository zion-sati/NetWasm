using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class LoopActiveBlockRetentionSelectorTests
{
    [Fact]
    public void SelectRetainsOnlyBlocksWithoutExistingDispatcherOwnership()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        state.DispatchersByNode.Add(2, ImmutableHashSet.Create(2, 4));
        var loop = CreateLoop(ImmutableHashSet.Create(1, 2, 3));
        var selector = Assert.IsAssignableFrom<ILoopActiveBlockRetentionSelector>(
            new LoopActiveBlockRetentionSelector());

        var retained = selector.Select(state, loop);

        Assert.Equal(ImmutableHashSet.Create(1, 3), retained);
    }

    [Fact]
    public void SelectRetainsTheWholeCoreWithoutDispatcherOwnership()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var loop = CreateLoop(ImmutableHashSet.Create(1, 2));
        var selector = Assert.IsAssignableFrom<ILoopActiveBlockRetentionSelector>(
            new LoopActiveBlockRetentionSelector());

        var retained = selector.Select(state, loop);

        Assert.Equal(loop.CoreComponent, retained);
    }

    [Fact]
    public void SelectRejectsNullInputs()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var loop = CreateLoop(ImmutableHashSet.Create(1));
        var selector = Assert.IsAssignableFrom<ILoopActiveBlockRetentionSelector>(
            new LoopActiveBlockRetentionSelector());

        Assert.Throws<ArgumentNullException>(() => selector.Select(null!, loop));
        Assert.Throws<ArgumentNullException>(() => selector.Select(state, null!));
    }

    private static LoopRegion CreateLoop(ImmutableHashSet<int> core)
    {
        return new LoopRegion(
            Header: 1,
            Condition: 2,
            Continue: 3,
            ConditionExit: 4,
            Exit: 5,
            CoreComponent: core,
            BodyComponent: core,
            ConditionExitComponent: ImmutableHashSet<int>.Empty);
    }
}
