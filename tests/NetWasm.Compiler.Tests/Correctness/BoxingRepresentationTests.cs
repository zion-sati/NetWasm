namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class BoxingRepresentationTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.BoxedRepresentation);

    [Theory]
    [MemberData(nameof(Cells))]
    public void NullableEnumAndReferenceStructBoxesPreserveValueIdentityAndMutation(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
