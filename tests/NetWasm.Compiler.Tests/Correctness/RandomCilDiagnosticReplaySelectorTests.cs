// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilDiagnosticReplaySelectorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(1399, 199)]
    public void SelectCaseMapsExactInputToOwningShardCase(int input, int expectedCase)
    {
        Assert.Equal(expectedCase, RandomCilDiagnosticReplaySelector.SelectCase(input, 1400, 200));
    }

    [Theory]
    [InlineData(1400, 1400, 200)]
    [InlineData(0, 1401, 200)]
    public void SelectCaseRejectsInvalidMappings(int input, int inputCount, int caseCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RandomCilDiagnosticReplaySelector.SelectCase(input, inputCount, caseCount));
    }
}
