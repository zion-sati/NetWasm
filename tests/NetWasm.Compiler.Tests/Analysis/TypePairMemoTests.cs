// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

// The type relationship classifier's memo: one byte per ordered pair of types, kept compact for the hundred million
// pairs a large program asks, read without a lock while other threads add.
public sealed class TypePairMemoTests
{
    private static readonly AssemblyIdentity Assembly = new("PairMemo");

    private static CliTypeIdentity[] Types(int count) =>
        [.. Enumerable.Range(0, count).Select(index => CliTypeIdentity.Named(Assembly, "Tests", $"T{index}", isValueType: false))];

    [Fact]
    public void EveryPairIsRememberedAcrossGrowthWithItsByte()
    {
        var types = Types(400);
        var memo = new TypePairMemo();
        long added = 0;
        for (var first = 0; first < types.Length; first++)
        {
            for (var second = first; second < types.Length; second += 3)
            {
                memo.Add(types[first], types[second], (byte)((first + second) % 8));
                added++;
            }
        }

        Assert.Equal(added, memo.Count);
        for (var first = 0; first < types.Length; first++)
        {
            for (var second = first; second < types.Length; second += 3)
            {
                Assert.True(memo.TryGet(types[first], types[second], out var value));
                Assert.Equal((byte)((first + second) % 8), value);
            }
        }
    }

    // A pair is ordered, an unknown pair or type misses, a zero byte (no relationship) is an answer, and a pair keeps
    // its first byte.
    [Fact]
    public void PairsAreOrderedAndUnknownPairsMiss()
    {
        var types = Types(3);
        var memo = new TypePairMemo();

        memo.Add(types[0], types[1], 0);
        memo.Add(types[0], types[1], 5);

        Assert.True(memo.TryGet(types[0], types[1], out var value));
        Assert.Equal(0, value);
        Assert.False(memo.TryGet(types[1], types[0], out _));
        Assert.False(memo.TryGet(types[0], types[2], out _));
        Assert.False(memo.TryGet(types[2], types[2], out _));
        Assert.Equal(1, memo.Count);
    }

    [Fact]
    public void ConcurrentAddsAndReadsAgree()
    {
        var types = Types(600);
        var memo = new TypePairMemo();

        Parallel.For(0, types.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, first =>
        {
            for (var second = 0; second < types.Length; second++)
            {
                memo.Add(types[first], types[second], (byte)(first ^ second));
                if (memo.TryGet(types[second], types[first], out var other))
                {
                    Assert.Equal((byte)(first ^ second), other);
                }
            }
        });

        Assert.Equal(600L * 600, memo.Count);
        for (var first = 0; first < types.Length; first++)
        {
            for (var second = 0; second < types.Length; second++)
            {
                Assert.True(memo.TryGet(types[first], types[second], out var value));
                Assert.Equal((byte)(first ^ second), value);
            }
        }
    }
}
