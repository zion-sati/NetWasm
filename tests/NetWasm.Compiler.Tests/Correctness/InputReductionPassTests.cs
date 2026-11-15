// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class InputReductionPassTests
{
    [Fact]
    public void RemovesOneInputPerCandidate()
    {
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var candidates = new InputReductionPass().Generate(original).ToArray();

        Assert.Equal(original.Inputs.Length, candidates.Length);
        Assert.All(candidates, candidate =>
            Assert.Equal(original.Inputs.Length - 1, candidate.Inputs.Length));
    }
}
