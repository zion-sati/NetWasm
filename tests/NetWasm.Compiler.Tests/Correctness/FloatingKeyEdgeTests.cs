namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class FloatingKeyEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingKeys);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void ComparersAndCollectionsRespectFloatingKeyEquivalence(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
