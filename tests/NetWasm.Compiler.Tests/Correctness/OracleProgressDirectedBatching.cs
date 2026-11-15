// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal static class OracleProgressDirectedBatching
{
    public static (
        ImmutableArray<int> Left,
        ImmutableArray<int> Right) Split(
        ImmutableArray<int> batch,
        int completed)
    {
        if (batch.Length < 2)
        {
            throw new ArgumentException(
                "A retry split requires at least two inputs.",
                nameof(batch));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(completed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            completed,
            batch.Length);

        var splitIndex = completed == 0
            ? 1
            : Math.Min(completed, batch.Length - 1);
        return (
            batch.AsSpan(0, splitIndex).ToArray().ToImmutableArray(),
            batch.AsSpan(splitIndex, batch.Length - splitIndex)
                .ToArray()
                .ToImmutableArray());
    }
}
