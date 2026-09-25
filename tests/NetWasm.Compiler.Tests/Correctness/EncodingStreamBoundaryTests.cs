namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class EncodingStreamBoundaryTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.EncodingStreams);

    [Theory]
    [MemberData(nameof(Cells))]
    public void IncrementalCodecsAndShortStreamsPreserveCountsStateAndOwnership(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
