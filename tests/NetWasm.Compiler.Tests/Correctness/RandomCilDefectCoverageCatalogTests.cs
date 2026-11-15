// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilDefectCoverageCatalogTests
{
    [Fact]
    public void EveryDefectNeighborhoodIsRetainedForEveryExhaustiveSeed()
    {
        var matrix = new RandomCilInteractionMatrix();
        var cases = matrix.GenerateExhaustive();
        var requirements = RandomCilDefectCoverageCatalog.Requirements;

        Assert.Equal(
            requirements.Length,
            requirements.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(requirements, requirement =>
        {
            Assert.False(string.IsNullOrWhiteSpace(requirement.FirstIncorrectLayer));
            Assert.False(string.IsNullOrWhiteSpace(requirement.FocusedRegression));
            Assert.NotEmpty(requirement.RequiredNeighborhoods);
            foreach (var neighborhood in requirement.RequiredNeighborhoods)
            {
                Assert.All(neighborhood, level => Assert.Contains(
                    matrix.Dimensions,
                    dimension => dimension.Name == level.Key &&
                                 dimension.Levels.Contains(level.Value)));
                foreach (var seed in matrix.Seeds)
                {
                    Assert.Contains(cases, testCase =>
                        testCase.Seed == seed &&
                        neighborhood.All(level =>
                            testCase.Levels[level.Key] == level.Value));
                }
            }
        });
    }
}
