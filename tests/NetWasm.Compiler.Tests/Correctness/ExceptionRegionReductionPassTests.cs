// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ExceptionRegionReductionPassTests
{
    [Fact]
    public void RemovesAClauseWithItsFilterOrHandlerBlocksAndKeepsValidCandidates()
    {
        var program = new RandomCilGenerator().GenerateExceptionHandling(73);
        var original = new GeneratedCilReductionCase(program, [1]);
        var validator = new GeneratedCilValidator();
        var candidates = new ExceptionRegionReductionPass().Generate(original)
            .Where(candidate =>
            {
                try
                {
                    validator.Validate(candidate.Program);
                    return true;
                }
                catch (InvalidDataException)
                {
                    return false;
                }
            })
            .ToArray();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate => Assert.True(
            candidate.Program.ExceptionRegions.Length <
            program.ExceptionRegions.Length));
    }
}
