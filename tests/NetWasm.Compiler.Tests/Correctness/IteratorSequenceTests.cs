namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class IteratorSequenceTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.IteratorOrder);

    [Theory]
    [MemberData(nameof(Cells))]
    public void EnumerationFailuresEarlyExitsAndRepeatedUsePreserveExactCleanupAndIdentity(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
