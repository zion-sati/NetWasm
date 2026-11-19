// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Field instructions naming static fields drop their object and access the static, as CoreCLR does; the fixture
// runs the same IL on desktop .NET and on the linked runtime.
public sealed class StaticFieldInstanceAccessTests(CorrectnessTestRunner runner) : EmittedAssemblyTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells =>
        CorpusCaseTestData.Cells(CorpusCaseTestData.StaticFieldInstanceAccess);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void FieldInstructionsOnStaticFieldsDropTheirObject(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell, new StaticFieldInstanceAccessFixtureBuilder());
}
