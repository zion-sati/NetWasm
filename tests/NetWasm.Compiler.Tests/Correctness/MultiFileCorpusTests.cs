namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class MultiFileCorpusTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.MultiFile);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void SeparateSourceFilesPreserveTheirFileLocalTypesAndScopes(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
