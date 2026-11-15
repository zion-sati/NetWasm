// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class DispatchRelationshipTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.DispatchRelationships);
    public static TheoryData<string, string> GenericCells => CorpusCaseTestData.Cells(CorpusCaseTestData.GenericRecursion);

    [Theory]
    [MemberData(nameof(GenericCells))]
    public void RecursiveAndVirtualGenericCallsPreserveClosedSubstitutionsAndState(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(Cells))]
    public void ExactVariantConstrainedAndDelegateCallsPreserveSlotsAndState(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
