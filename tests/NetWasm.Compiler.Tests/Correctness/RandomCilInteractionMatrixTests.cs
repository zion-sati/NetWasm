// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilInteractionMatrixTests
{
    [Fact]
    public void ExhaustiveCorpusContainsEveryFullTupleOrderingAndSeed()
    {
        var subject = new RandomCilInteractionMatrix();
        var first = subject.GenerateExhaustive();
        var expected = subject.Dimensions.Aggregate(
            subject.Seeds.Length,
            (count, dimension) => checked(count * dimension.Levels.Length));

        Assert.Equal(RandomCilQualificationWorkload.ExhaustiveProgramCount, expected);
        Assert.Equal(expected, first.Length);
        Assert.Equal(first.Length, first.Select(item => item.Id).Distinct().Count());
        Assert.All(first, item => Assert.Equal(
            subject.Dimensions.Length,
            item.Levels.Count));
        Assert.Equal(subject.Seeds[0], first[0].Seed);
        Assert.Equal(subject.Seeds[^1], first[^1].Seed);
        Assert.Equal(subject.Dimensions[0].Levels[0], first[0].Levels["numeric"]);
        Assert.Equal(subject.Dimensions[^1].Levels[^1], first[^1].Levels["order"]);
    }
}
