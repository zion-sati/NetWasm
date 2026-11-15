// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RttiArraySemanticTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.RttiArrays);
    public static TheoryData<string, string> ShapeCells => CorpusCaseTestData.Cells(CorpusCaseTestData.ArrayShapes);

    [Theory]
    [MemberData(nameof(ShapeCells))]
    public void RectangularAndJaggedArraysPreserveShapeCovarianceAndFailureState(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(Cells))]
    public void CompilerPreservesChecksAfterArrayConversions(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
