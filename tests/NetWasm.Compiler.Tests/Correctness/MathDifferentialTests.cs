namespace NetWasm.Compiler.Tests.Correctness;

public sealed class MathDifferentialTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.Math);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void MathAndMathFMatchTheDesktopOracleAtBoundaryAndRepresentativeInputs(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
