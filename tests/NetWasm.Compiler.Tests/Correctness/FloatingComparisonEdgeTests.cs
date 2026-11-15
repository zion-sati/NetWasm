// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class FloatingComparisonEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingComparisons);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void OperatorsBranchesEqualsAndCompareToRespectTheirDistinctContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
