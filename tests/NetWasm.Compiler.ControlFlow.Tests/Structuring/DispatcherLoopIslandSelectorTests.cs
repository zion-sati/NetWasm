// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class DispatcherLoopIslandSelectorTests
{
    [Fact]
    public void SelectReturnsCompleteOutermostAndDisjointLoopComponents()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        state.LoopsByHeader.Add(2, Loop(2, [2, 3], [2, 3, 4], [5]));
        state.LoopsByHeader.Add(3, Loop(3, [3], [3, 4], []));
        state.LoopsByHeader.Add(6, Loop(6, [6, 7], [6, 7], []));
        var available = ImmutableHashSet.Create(0, 2, 3, 4, 5, 6, 7);

        var selected = Select(
            new DispatcherLoopIslandSelector(),
            state,
            0,
            available,
            available,
            available);

        Assert.True(selected.SetEquals(ImmutableHashSet.Create(2, 3, 4, 5, 6, 7)));
    }

    [Fact]
    public void SelectLeavesPartiallyOverlappingLoopsInTheDispatcher()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        state.LoopsByHeader.Add(2, Loop(2, [2, 3], [2, 3], []));
        state.LoopsByHeader.Add(3, Loop(3, [3, 4], [3, 4], []));
        var available = ImmutableHashSet.Create(0, 2, 3, 4);

        var selected = Select(
            new DispatcherLoopIslandSelector(),
            state,
            0,
            available,
            available,
            available);

        Assert.Empty(selected);
    }

    [Theory]
    [InlineData(SelectionRejection.OutsideComponent)]
    [InlineData(SelectionRejection.OutsideAllowed)]
    [InlineData(SelectionRejection.ExceptionFiltered)]
    [InlineData(SelectionRejection.ContainsEntry)]
    [InlineData(SelectionRejection.FallbackDispatcher)]
    public void SelectRejectsAnIneligibleLoopComponent(SelectionRejection rejection)
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        state.LoopsByHeader.Add(2, Loop(2, [2, 3], [2, 3], []));
        var component = ImmutableHashSet.Create(0, 2, 3);
        var allowed = component;
        var owned = component;
        var entry = 0;
        switch (rejection)
        {
            case SelectionRejection.OutsideComponent:
                component = component.Remove(3);
                break;
            case SelectionRejection.OutsideAllowed:
                allowed = allowed.Remove(3);
                break;
            case SelectionRejection.ExceptionFiltered:
                owned = owned.Remove(3);
                break;
            case SelectionRejection.ContainsEntry:
                entry = 2;
                break;
            case SelectionRejection.FallbackDispatcher:
                state.DispatchersByNode.Add(3, ImmutableHashSet.Create(3));
                break;
        }

        var selected = Select(
            new DispatcherLoopIslandSelector(),
            state,
            entry,
            component,
            allowed,
            owned);

        Assert.Empty(selected);
    }

    [Fact]
    public void SelectRequiresAnOwnedDispatcherEntry()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        state.LoopsByHeader.Add(2, Loop(2, [2, 3], [2, 3], []));
        var available = ImmutableHashSet.Create(0, 2, 3);
        var actor = new DispatcherLoopIslandSelector();

        var absent = Select(actor, state, null, available, available, available);
        var unowned = Select(actor, state, 0, available, available, available.Remove(0));

        Assert.Empty(absent);
        Assert.Empty(unowned);
    }

    [Fact]
    public void SelectRejectsNullInputs()
    {
        var actor = new DispatcherLoopIslandSelector();
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var blocks = ImmutableHashSet.Create(0);

        Assert.Throws<ArgumentNullException>(() =>
            Select(actor, null!, 0, blocks, blocks, blocks));
        Assert.Throws<ArgumentNullException>(() =>
            Select(actor, state, 0, null!, blocks, blocks));
        Assert.Throws<ArgumentNullException>(() =>
            Select(actor, state, 0, blocks, null!, blocks));
        Assert.Throws<ArgumentNullException>(() =>
            Select(actor, state, 0, blocks, blocks, null!));
    }

    private static LoopRegion Loop(
        int header,
        ImmutableHashSet<int> core,
        ImmutableHashSet<int> body,
        ImmutableHashSet<int> conditionExit) => new(
        header,
        header,
        header,
        conditionExit.FirstOrDefault(-1),
        -1,
        core,
        body,
        conditionExit);

    private static ImmutableHashSet<int> Select<TSelector>(
        TSelector selector,
        ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        ImmutableHashSet<int> owned)
        where TSelector : IDispatcherLoopIslandSelector =>
        selector.Select(state, entry, component, allowed, owned);

    public enum SelectionRejection
    {
        OutsideComponent,
        OutsideAllowed,
        ExceptionFiltered,
        ContainsEntry,
        FallbackDispatcher,
    }
}
