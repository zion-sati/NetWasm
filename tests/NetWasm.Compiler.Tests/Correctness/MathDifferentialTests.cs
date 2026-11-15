// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class MathDifferentialTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.Math);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void MathAndMathFMatchTheDesktopOracleAtBoundaryAndRepresentativeInputs(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
