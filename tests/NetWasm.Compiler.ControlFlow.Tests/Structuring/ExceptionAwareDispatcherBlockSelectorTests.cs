using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ExceptionAwareDispatcherBlockSelectorTests
{
    [Fact]
    public void SelectExcludesARegisteredExceptionExtent()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        state.ExceptionGroupsByEntry.Add(0, CreateExceptionGroup());
        var component = ImmutableHashSet.Create(0);
        var selector = new ExceptionAwareDispatcherBlockSelector();

        var selected = ((IExceptionAwareDispatcherBlockSelector)selector).Select(
            state,
            component,
            component);

        Assert.Empty(selected);
    }

    [Fact]
    public void SelectRetainsAComponentWithoutARegisteredExceptionEntry()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var component = ImmutableHashSet.Create(0);
        var selector = new ExceptionAwareDispatcherBlockSelector();

        var selected = ((IExceptionAwareDispatcherBlockSelector)selector).Select(
            state,
            component,
            component);

        Assert.True(selected.SetEquals(component));
    }

    [Fact]
    public void SelectRetainsBlocksAfterARegisteredExceptionExtent()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        state.ExceptionGroupsByEntry.Add(0, CreateExceptionGroup());
        var component = state.Graph.Successors.Keys.ToImmutableHashSet();
        var selector = new ExceptionAwareDispatcherBlockSelector();

        var selected = ((IExceptionAwareDispatcherBlockSelector)selector).Select(
            state,
            component,
            component);

        Assert.DoesNotContain(0, selected);
        Assert.Equal(component.Count - 1, selected.Count);
    }

    [Fact]
    public void SelectRetainsBlocksBeforeARegisteredExceptionExtent()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        state.ExceptionGroupsByEntry.Add(11, CreateExceptionGroup(11));
        var component = ImmutableHashSet.Create(0, 11);
        var selector = new ExceptionAwareDispatcherBlockSelector();

        var selected = ((IExceptionAwareDispatcherBlockSelector)selector).Select(
            state,
            component,
            component);

        Assert.Equal([0], selected);
    }

    [Fact]
    public void SelectRejectsNullInputs()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var component = ImmutableHashSet.Create(0);
        var selector = new ExceptionAwareDispatcherBlockSelector();

        Assert.Throws<ArgumentNullException>(() =>
            ((IExceptionAwareDispatcherBlockSelector)selector).Select(null!, component, component));
        Assert.Throws<ArgumentNullException>(() =>
            ((IExceptionAwareDispatcherBlockSelector)selector).Select(state, null!, component));
        Assert.Throws<ArgumentNullException>(() =>
            ((IExceptionAwareDispatcherBlockSelector)selector).Select(state, component, null!));
    }

    private static StructuredExceptionGroupDraft CreateExceptionGroup(int tryOffset = 0)
    {
        var region = new CilExceptionRegion(
            CilExceptionRegionKind.Finally,
            TryOffset: tryOffset,
            TryLength: 1,
            HandlerOffset: tryOffset,
            HandlerLength: 1,
            CatchType: null,
            FilterOffset: null);
        var clause = new StructuredExceptionClauseDraft(
            region,
            StructuredSequenceDraft.Empty,
            FilterBody: null);
        return new StructuredExceptionGroupDraft(
            TryOffset: tryOffset,
            TryLength: 1,
            ImmutableArray<StructuredExceptionPartDraft>.Empty,
            ImmutableArray.Create(clause),
            ImmutableArray<StructuredExceptionContinuationDraft>.Empty);
    }
}
