namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class AsyncLifetimeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.AsyncLifetime);
    public static TheoryData<string, string> EnumerationCells => CorpusCaseTestData.Cells(CorpusCaseTestData.AsyncEnumeration);
    public static TheoryData<string, string> ImplicitExceptionCells =>
        CorpusCaseTestData.Cells(CorpusCaseTestData.ImplicitExceptionLifetime);

    [Theory]
    [MemberData(nameof(ImplicitExceptionCells))]
    public void ImplicitExceptionsRemainManagedAcrossHeapAndAsyncStorage(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.ComponentExportExceptionIdentity)]
    public void EscapingComponentExceptionsDoNotPromiseDesktopTypeIdentity() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory]
    [MemberData(nameof(Cells))]
    public void ExplicitSuspensionPreservesRealRootsResultsFailuresCancellationAndCleanup(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(EnumerationCells))]
    public void AsyncEnumerationPreservesPendingCancellationFailureIdentityAndExactDisposal(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
