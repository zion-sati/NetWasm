// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilInteractionProgramSlicerTests
{
    [Fact]
    public void PartitionsAQualificationShardWithoutLossOrDuplication()
    {
        var planner = new RandomCilInteractionShardPlanner(
            new RandomCilInteractionMatrix());
        var shard = planner.CreateShards()[0];

        var slices = RandomCilInteractionProgramSlicer.Create(shard, 100);

        Assert.Equal(
            (shard.Cases.Length + 99) / 100,
            slices.Length);
        Assert.All(slices, slice => Assert.Equal(shard.Index, slice.Shard.Index));
        Assert.All(slices, slice => Assert.InRange(slice.Shard.Cases.Length, 1, 100));
        Assert.Equal(
            shard.Cases,
            slices.SelectMany(slice => slice.Shard.Cases));
        Assert.Equal(
            Enumerable.Range(0, slices.Length),
            slices.Select(slice => slice.Index));
        Assert.Equal(
            Enumerable.Range(0, slices.Length).Select(index => index * 100),
            slices.Select(slice => slice.Offset));
    }

    [Fact]
    public void RejectsNonPositiveProgramCapacity()
    {
        var shard = new RandomCilInteractionShard(
            0,
            []);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => RandomCilInteractionProgramSlicer.Create(shard, 0));
    }
}
