// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ValidCSharpFuzzTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void AcceptedGrammarProgramsMatchDesktopDotNet()
    {
        var requested = Environment.GetEnvironmentVariable(
            "NETWASM_VALID_FUZZ_SEED");
        var seeds = requested is null
            ? (int[])[0x10203, 0x40506, 0x70809, 0xA0B0C]
            : (int[])[int.Parse(
                requested,
                System.Globalization.CultureInfo.InvariantCulture)];
        foreach (var generatedCase in runner.GenerateFuzzCases(seeds))
        {
            runner.RunGenerated(generatedCase);
        }
    }
}
