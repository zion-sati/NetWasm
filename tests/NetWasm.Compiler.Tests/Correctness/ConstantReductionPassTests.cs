// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ConstantReductionPassTests
{
    [Fact]
    public void ReplacesConstantsOnlyWithLowerComplexityCanonicalValues()
    {
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var score = GeneratedCilReductionScore.Measure(original);
        var candidates = new ConstantReductionPass().Generate(original).ToArray();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate => Assert.True(
            GeneratedCilReductionScore.Measure(candidate).CompareTo(score) < 0));
    }
}
