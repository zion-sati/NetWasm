namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class RttiArraySemanticTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.RttiArrays);
    public static TheoryData<string, string> ShapeCells => CorpusCaseTestData.Cells(CorpusCaseTestData.ArrayShapes);

    [Theory]
    [MemberData(nameof(ShapeCells))]
    public void RectangularAndJaggedArraysPreserveShapeCovarianceAndFailureState(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(Cells))]
    public void CompilerPreservesChecksAfterArrayConversions(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
