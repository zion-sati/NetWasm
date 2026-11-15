// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class IteratorSequenceTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.IteratorOrder);

    [Theory]
    [MemberData(nameof(Cells))]
    public void EnumerationFailuresEarlyExitsAndRepeatedUsePreserveExactCleanupAndIdentity(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
