// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class DispatcherBranchTablePlannerTests
{
    private static readonly ImmutableArray<int> TopologicalOrder =
        [7, 5, 6, 3, 2, 1, 4, 0];

    private readonly DispatcherBranchTablePlanner _planner = new();

    [Fact]
    public void DenseForwardChainUsesReverseLayoutAndDirectTransitions()
    {
        var dispatcher = Dispatcher(Enumerable.Range(0, 32)
            .Select(index => Block(index, index == 31 ? null : index + 1)));

        var plan = Assert.IsType<DispatcherBranchTablePlan>(
            Plan(dispatcher));

        Assert.Equal(Enumerable.Range(0, 32).Reverse(),
            plan.Cases.Select(item => item.Target));
        Assert.Equal(0U, plan.Targets[0]);
        Assert.Equal(31U, plan.Targets[31]);
        var first = Assert.Single(plan.Cases.Where(item => item.Target == 0));
        Assert.Equal(0U, first.WhenTrue!.DirectBranchDepth);
        Assert.Null(first.WhenFalse);
        var terminal = Assert.Single(plan.Cases.Where(item => item.Target == 31));
        Assert.Null(terminal.WhenTrue);
    }

    [Fact]
    public void DenseConditionalForkPlansBothDirectDepths()
    {
        var blocks = Enumerable.Range(0, 32)
            .Select(index => index == 0
                ? Block(index, 1, 2)
                : Block(index, null));

        var plan = Assert.IsType<DispatcherBranchTablePlan>(
            Plan(Dispatcher(blocks)));
        var fork = Assert.Single(plan.Cases.Where(item => item.Target == 0));

        Assert.Equal(0U, fork.WhenTrue!.DirectBranchDepth);
        Assert.Equal(1U, fork.WhenFalse!.DirectBranchDepth);
    }

    [Fact]
    public void DenseCycleRetainsRedispatchBackedge()
    {
        var blocks = Enumerable.Range(0, 32)
            .Select(index => Block(index, (index + 1) % 32));

        var plan = Assert.IsType<DispatcherBranchTablePlan>(
            Plan(Dispatcher(blocks)));
        var backedge = Assert.Single(plan.Cases.Where(item => item.Target == 31));

        Assert.Equal(new StructuredBlockId(0), backedge.WhenTrue!.Target);
        Assert.Null(backedge.WhenTrue.DirectBranchDepth);
    }

    [Fact]
    public void SmallAcyclicDispatcherUsesTopologicalLayout()
    {
        var blocks = new[]
        {
            Block(0, 4),
            Block(1, 2),
            Block(2, 3),
            Block(3, 6),
            Block(4, 1),
            Block(5, 7),
            Block(6, 5),
            Block(7, null),
        };

        var plan = Assert.IsType<DispatcherBranchTablePlan>(
            Plan(Dispatcher(blocks)));

        Assert.Equal(TopologicalOrder,
            plan.Cases.Select(item => item.Target));
        Assert.All(plan.Cases.Where(item => item.WhenTrue is not null),
            item => Assert.NotNull(item.WhenTrue!.DirectBranchDepth));
    }

    [Fact]
    public void SmallCyclicDispatcherUsesLinearFallback()
    {
        var blocks = Enumerable.Range(0, 8)
            .Select(index => Block(index, (index + 1) % 8));

        Assert.Null(Plan(Dispatcher(blocks)));
    }

    [Fact]
    public void EmptyExitCanBeThreadedDirectly()
    {
        var exit = new StructuredDispatcherExit(
            new StructuredBlockId(32),
            StructuredSequence.Empty);
        var blocks = Enumerable.Range(0, 32)
            .Select(index => Block(index, index == 0 ? 32 : null));

        var plan = Assert.IsType<DispatcherBranchTablePlan>(
            Plan(Dispatcher(blocks, exit)));
        var entry = Assert.Single(plan.Cases.Where(item => item.Target == 0));

        Assert.Equal(31U, entry.WhenTrue!.DirectBranchDepth);
        // A branch straight to an exit leaves the dispatcher; one to a block does not.
        Assert.True(entry.WhenTrue.LeavesDispatcher);
        Assert.All(plan.Cases.Where(item => item.Block is not null && item.WhenTrue is { DirectBranchDepth: not null }
                && item.WhenTrue.Target.Value != 32),
            item => Assert.False(item.WhenTrue!.LeavesDispatcher));
    }

    [Fact]
    public void PopulatedExitRetainsRedispatchTransition()
    {
        var exit = new StructuredDispatcherExit(
            new StructuredBlockId(32),
            new StructuredSequence([new StructuredCode(Occurrence(40))]));
        var blocks = Enumerable.Range(0, 32)
            .Select(index => Block(index, index == 0 ? 32 : null));

        var plan = Assert.IsType<DispatcherBranchTablePlan>(
            Plan(Dispatcher(blocks, exit)));
        var entry = Assert.Single(plan.Cases.Where(item => item.Target == 0));

        Assert.Null(entry.WhenTrue!.DirectBranchDepth);
    }

    [Fact]
    public void SmallDispatcherUsesLinearFallback()
    {
        Assert.Null(Plan(Dispatcher(
            Enumerable.Range(0, 31).Select(index => Block(index, null)))));
    }

    [Fact]
    public void SparseDispatcherUsesLinearFallback()
    {
        var blocks = Enumerable.Range(0, 31)
            .Append(128)
            .Select(index => Block(index, null));

        Assert.Null(Plan(Dispatcher(blocks)));
    }

    [Fact]
    public void NegativeTargetUsesLinearFallback()
    {
        var blocks = Enumerable.Range(0, 31)
            .Prepend(-1)
            .Select(index => Block(index, null));

        Assert.Null(Plan(Dispatcher(blocks)));
    }

    [Fact]
    public void DuplicateTargetUsesLinearFallback()
    {
        var duplicate = new StructuredDispatcherExit(
            new StructuredBlockId(0),
            StructuredSequence.Empty);

        Assert.Null(Plan(Dispatcher(
            Enumerable.Range(0, 32).Select(index => Block(index, null)),
            duplicate)));
    }

    [Fact]
    public void RejectsMissingDispatcher()
    {
        Assert.Throws<ArgumentNullException>(() => Plan(null!));
    }

    private static StructuredDispatcher Dispatcher(
        IEnumerable<StructuredDispatcherBlock> blocks,
        params StructuredDispatcherExit[] exits) => new(
        new StructuredBlockId(0),
        blocks.ToImmutableArray(),
        exits.ToImmutableArray());

    private DispatcherBranchTablePlan? Plan(StructuredDispatcher dispatcher) =>
        ((IDispatcherBranchTablePlanner)_planner).Plan(dispatcher);

    private static StructuredDispatcherBlock Block(
        int index,
        int? whenTrue,
        int? whenFalse = null) => new(
        Occurrence(index),
        whenTrue is { } trueTarget ? new StructuredBlockId(trueTarget) : null,
        whenFalse is { } falseTarget ? new StructuredBlockId(falseTarget) : null);

    private static StructuredBlockOccurrence Occurrence(int index) => new(
        new StructuredBlockId(index),
        StructuredBlockRole.Owner);
}
