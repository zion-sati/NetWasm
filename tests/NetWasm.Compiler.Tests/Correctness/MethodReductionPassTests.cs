// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class MethodReductionPassTests
{
    [Fact]
    public void RemovesOneValidatedSupportingMethodPerCandidate()
    {
        var original = GeneratedCilReductionTestPrograms.StraightLine() with
        {
            SupportingMethods =
            [
                GeneratedCilReductionTestPrograms.StraightLine().Program,
                GeneratedCilReductionTestPrograms.StraightLine().Program with
                {
                    Seed = 18,
                },
            ],
        };
        var candidates = new MethodReductionPass().Generate(original).ToArray();

        Assert.Equal(2, candidates.Length);
        Assert.All(candidates, candidate =>
            Assert.Single(candidate.SupportingMethods));
        var validator = new GeneratedCilValidator();
        Assert.All(candidates, candidate =>
            Assert.All(candidate.SupportingMethods, validator.Validate));
    }
}
