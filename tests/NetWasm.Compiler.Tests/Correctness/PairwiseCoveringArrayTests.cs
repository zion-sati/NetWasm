// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class PairwiseCoveringArrayTests
{
    [Fact]
    public void GenerateIsDeterministicAndCoversEveryPair()
    {
        var dimensions = ImmutableArray.Create(
            new CorpusDimension("first", ["a", "b", "c"]),
            new CorpusDimension("second", ["d", "e", "f"]),
            new CorpusDimension("third", ["g", "h", "i"]));
        var subject = new PairwiseCoveringArray();

        var first = subject.Generate(dimensions, 12345);
        var second = subject.Generate(dimensions, 12345);

        Assert.Equal(
            first.Select(Describe),
            second.Select(Describe));
        for (var left = 0; left < dimensions.Length; left++)
        {
            for (var right = left + 1; right < dimensions.Length; right++)
            {
                foreach (var leftLevel in dimensions[left].Levels)
                {
                    foreach (var rightLevel in dimensions[right].Levels)
                    {
                        Assert.Contains(first, values =>
                            values[dimensions[left].Name] == leftLevel &&
                            values[dimensions[right].Name] == rightLevel);
                    }
                }
            }
        }
        Assert.InRange(first.Length, 9, 12);
    }

    private static string Describe(ImmutableDictionary<string, string> values) =>
        string.Join('|', values.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Key + '=' + item.Value));

    [Fact]
    public void GenerateRejectsInvalidDimensions()
    {
        var subject = new PairwiseCoveringArray();

        Assert.Throws<ArgumentException>(() => subject.Generate([], 0));
        Assert.Throws<ArgumentException>(() => subject.Generate(
            [new("same", ["a"]), new("same", ["b"])], 0));
        Assert.Throws<ArgumentException>(() => subject.Generate(
            [new("left", ["a", "a"]), new("right", ["b"])], 0));
    }
}
