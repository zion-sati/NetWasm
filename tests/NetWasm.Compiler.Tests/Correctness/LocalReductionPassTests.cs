// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LocalReductionPassTests
{
    [Fact]
    public void RemovesOnlyAnUnusedTrailingLocal()
    {
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var candidate = Assert.Single(new LocalReductionPass().Generate(original));

        Assert.Empty(candidate.Program.Locals);
    }
}
