namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class NumericTransportEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.NumericTransport);
    public static TheoryData<string, string> ConversionCells => CorpusCaseTestData.Cells(CorpusCaseTestData.ConversionTransport);
    public static TheoryData<string, string> AsyncCells => CorpusCaseTestData.Cells(CorpusCaseTestData.AsyncNumericTransport);
    public static TheoryData<string, string> AsyncConversionCells => CorpusCaseTestData.Cells(CorpusCaseTestData.AsyncConversionTransport);

    [Theory]
    [MemberData(nameof(AsyncConversionCells))]
    public void AsyncConversionsPreserveSeparateSourceFloatingAndRoundTripContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.ComponentExportExceptionIdentity)]
    public void ComponentExportsCannotExposeArbitraryManagedExceptionIdentity() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory]
    [MemberData(nameof(AsyncCells))]
    public void AsyncSuspensionPreservesNumericRepresentationsAndNativeWidth(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(ConversionCells))]
    public void ConversionBoundariesPreserveIndependentSourceFloatingAndRoundTripObservations(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(Cells))]
    public void NumericRepresentationsSurviveStorageAndCallBoundaries(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
