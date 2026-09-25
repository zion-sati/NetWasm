namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class FloatingComparisonEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingComparisons);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void OperatorsBranchesEqualsAndCompareToRespectTheirDistinctContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
