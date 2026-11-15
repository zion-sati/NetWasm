// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class InstructionReductionPassTests
{
    [Fact]
    public void ProducesChunkAndSingleInstructionCandidates()
    {
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var counts = new InstructionReductionPass().Generate(original)
            .Select(candidate => candidate.Program.Operations.Length == 0
                ? 0
                : candidate.Program.Blocks.Sum(block => block.Instructions.Length))
            .ToArray();

        Assert.Contains(3, counts);
        Assert.Contains(5, counts);
    }
}
