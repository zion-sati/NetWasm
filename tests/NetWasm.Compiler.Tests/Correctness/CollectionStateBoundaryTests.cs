namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class CollectionStateBoundaryTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.CollectionState);

    [Theory]
    [MemberData(nameof(Cells))]
    public void CollisionsMutationComparerFailuresAndViewsPreserveExactState(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
