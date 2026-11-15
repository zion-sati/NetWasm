// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class OracleInputBatchingTests
{
    [Fact]
    public void PartitionPreservesEveryInputInBoundedOrder()
    {
        var inputs = Enumerable.Range(0, 251).ToImmutableArray();

        var batches = OracleInputBatching.Partition(inputs).ToImmutableArray();

        Assert.Equal([100, 100, 51], batches.Select(batch => batch.Length));
        Assert.Equal(inputs, batches.SelectMany(batch => batch));
        Assert.All(batches, batch =>
            Assert.InRange(batch.Length, 1, OracleInputBatching.MaxInputsPerProcess));
    }

    [Fact]
    public void BisectPreservesOddBatchWithoutOverlap()
    {
        var inputs = Enumerable.Range(10, 5).ToImmutableArray();

        var (left, right) = OracleInputBatching.Bisect(inputs);

        Assert.Equal([10, 11], left.AsEnumerable());
        Assert.Equal([12, 13, 14], right.AsEnumerable());
    }
}
