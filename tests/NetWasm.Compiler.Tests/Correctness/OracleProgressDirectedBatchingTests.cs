// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class OracleProgressDirectedBatchingTests
{
    [Fact]
    public void ProgressSplitsAtTheExactCompletedCount()
    {
        var batch = Enumerable.Range(100, 100).ToImmutableArray();

        var (left, right) = OracleProgressDirectedBatching.Split(
            batch,
            completed: 96);

        Assert.Equal(batch[..96].ToArray(), left.ToArray());
        Assert.Equal(batch[96..].ToArray(), right.ToArray());
        Assert.Equal(batch.ToArray(), left.Concat(right).ToArray());
    }

    [Fact]
    public void ZeroProgressImmediatelyIsolatesTheFirstInput()
    {
        var batch = Enumerable.Range(150, 50).ToImmutableArray();

        var (left, right) = OracleProgressDirectedBatching.Split(
            batch,
            completed: 0);

        Assert.Equal([150], left.ToArray());
        Assert.Equal(batch[1..].ToArray(), right.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void RejectsProgressOutsideTheBatch(int completed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OracleProgressDirectedBatching.Split([1, 2], completed));
    }
}
