// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilInteractionFixtureFactoryTests
{
    [Fact]
    public void InteractionTemplatesOptIntoTheStateSafeBatchedDesktopOracle()
    {
        var fixture = new RandomCilInteractionFixtureFactory()
            .CreateTemplate(CilProfile.Debug, 1);

        Assert.True(fixture.SupportsBatchedOracle);
    }

    [Fact]
    public void TemplateContainsEveryPatchSlotAndEncodedBoundaryInput()
    {
        var factory = new RandomCilInteractionFixtureFactory();
        var fixture = factory.CreateTemplate(CilProfile.Release);

        Assert.Equal(1_400, fixture.Inputs.Length);
        Assert.Contains("Case000", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("Case199", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("BaselineRun", fixture.Source, StringComparison.Ordinal);
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
        Assert.True(fixture.ExecuteWasm64);
        Assert.True(fixture.ExecuteOptimizedWasm);
    }

    [Fact]
    public void PartialShardOnlyExecutesPopulatedSlots()
    {
        var matrix = new RandomCilInteractionMatrix();
        var planner = new RandomCilInteractionShardPlanner(matrix);
        var factory = new RandomCilInteractionFixtureFactory();

        var first = planner.CreateShards()[0];
        var partial = new RandomCilInteractionShard(
            first.Index,
            first.Cases.Take(80).ToArray().ToImmutableArray());
        var fixture = factory.CreateShard(CilProfile.Debug, partial);

        Assert.Equal(80 * factory.BoundaryInputs.Length, fixture.Inputs.Length);
    }

    [Fact]
    public void ExactReplayTemplateContainsOnlyOneReachablePatchSlot()
    {
        var factory = new RandomCilInteractionFixtureFactory();

        var fixture = factory.CreateTemplate(CilProfile.Debug, slotCount: 1);

        Assert.Equal(factory.BoundaryInputs.Length, fixture.Inputs.Length);
        Assert.Contains("Case000", fixture.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Case001", fixture.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchSlotsReserveManagedReferenceLocalForLifetimeInteractions()
    {
        var fixture = new RandomCilInteractionFixtureFactory().CreateTemplate(CilProfile.Debug, 1);

        Assert.Contains("var box = new Box { Value = value };", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("return box.Value == int.MinValue ? box.Value : value + loopBudget;", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("int loopBudget = value ^ input;", fixture.Source, StringComparison.Ordinal);
        Assert.Contains(" + loopBudget;", fixture.Source, StringComparison.Ordinal);
    }

}
