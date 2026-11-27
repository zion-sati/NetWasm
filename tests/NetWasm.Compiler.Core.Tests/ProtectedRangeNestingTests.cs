// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.Tests;

public sealed class ProtectedRangeNestingTests
{
    // [0,10) holds [2,5) and [5,8); [3,4) is inside [2,5); [12,14) is apart.
    [Fact]
    public void EachPositionGetsItsInnermostRangeAndEachRangeItsParent()
    {
        (int Start, int End)[] ranges = [(5, 8), (0, 10), (12, 14), (2, 5), (3, 4)];

        var nesting = ProtectedRangeNesting.Build(ranges, [0, 2, 3, 4, 5, 8, 10, 12, 13, 14]);

        Assert.NotNull(nesting);
        Assert.Equal([1, -1, -1, 1, 3], nesting.Parents);
        Assert.Equal([1, 3, 4, 3, 0, 1, -1, 2, 2, -1], nesting.Innermost);
    }

    [Fact]
    public void RangesThatOverlapWithoutNestingHaveNoNesting()
    {
        Assert.Null(ProtectedRangeNesting.Build([(0, 6), (4, 10)], [0, 5]));
    }

    [Fact]
    public void PositionsThatDoNotAscendHaveNoNesting()
    {
        Assert.Null(ProtectedRangeNesting.Build([(0, 6)], [3, 1]));
    }

    // An empty range holds no position and is no other range's parent.
    [Fact]
    public void AnEmptyRangeHoldsNothing()
    {
        var nesting = ProtectedRangeNesting.Build([(0, 6), (2, 2)], [1, 2, 3]);

        Assert.NotNull(nesting);
        Assert.Equal([-1, 0], nesting.Parents);
        Assert.Equal([0, 0, 0], nesting.Innermost);
    }

    // A deep chain: every position's innermost range is the deepest that holds it, in one sweep.
    [Fact]
    public void ADeepChainNestsEachRangeInThePreviousOne()
    {
        const int depth = 20_000;
        var ranges = Enumerable.Range(0, depth).Select(level => (level, 2 * depth - level)).ToArray();
        var positions = Enumerable.Range(0, 2 * depth).ToArray();

        var nesting = ProtectedRangeNesting.Build(ranges, positions);

        Assert.NotNull(nesting);
        Assert.Equal(-1, nesting.Parents[0]);
        Assert.All(Enumerable.Range(1, depth - 1), level => Assert.Equal(level - 1, nesting.Parents[level]));
        Assert.All(positions, position => Assert.Equal(Math.Min(position, Math.Min(depth - 1, 2 * depth - 1 - position)),
            nesting.Innermost[position]));
    }
}
