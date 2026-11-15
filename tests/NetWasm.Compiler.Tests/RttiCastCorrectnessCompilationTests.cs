// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

public sealed class RttiCastCorrectnessCompilationTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.RttiCasts);

    [Theory]
    [MemberData(nameof(Cells))]
    public void CompilerPreservesRuntimeCastAndArrayAssignmentSemantics(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
