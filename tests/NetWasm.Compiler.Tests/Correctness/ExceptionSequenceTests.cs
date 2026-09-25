namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class ExceptionSequenceTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.ExceptionOrder);
    public static TheoryData<string, string> RootCells => CorpusCaseTestData.Cells(CorpusCaseTestData.UnwindRoots);

    [Theory]
    [MemberData(nameof(Cells))]
    public void FilterSearchUnwindAndCleanupPreserveExactEventsAndExceptionIdentity(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    // Preserve the independent normal-continuation regression alongside the
    // registered matrix, which also covers pending exceptional unwinding.
    [Fact]
    public void NormalFinallyExecutesItsNestedCatchBeforeContinuing()
    {
        var manifest = CorpusCaseTestData.Get(CorpusCaseTestData.ExceptionOrder) with
        {
            CaseId = "flow.normal-finally-nested-catch",
            Inputs = [13],
            Expectations = [new(13, 42, null)],
            MatrixProfile = CorpusMatrixProfile.Fast,
            TestMethod = typeof(ExceptionSequenceTests).FullName + "." +
                nameof(NormalFinallyExecutesItsNestedCatchBeforeContinuing),
        };
        Run(manifest, "Release-Wasm32-Direct");
    }

    [Theory]
    [MemberData(nameof(RootCells))]
    public void RealCollectionPreservesExceptionLocalInteriorAndDisposalRootsDuringUnwind(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
