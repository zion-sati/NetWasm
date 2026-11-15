// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ControlFlowReductionPassTests
{
    [Fact]
    public void SimplifiesAnEdgeAndPrunesAtLeastOneOriginalBlock()
    {
        var program = new RandomCilGenerator().Generate(46);
        var original = new GeneratedCilReductionCase(program, [0]);
        var validator = new GeneratedCilValidator();
        var candidates = new ControlFlowReductionPass().Generate(original)
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

        Assert.Contains(candidates, candidate =>
            candidate.Program.Blocks.Length < program.Blocks.Length);
    }
}
