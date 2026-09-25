namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class FloatingArithmeticEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingArithmetic);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void RuntimeArithmeticPreservesExceptionalValuesRoundingAndZeroSigns(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
