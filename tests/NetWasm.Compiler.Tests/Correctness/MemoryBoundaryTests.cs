namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class MemoryBoundaryTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> UnsafeCells => CorpusCaseTestData.Cells(CorpusCaseTestData.UnsafeBoundaries);
    public static TheoryData<string, string> LayoutCells => CorpusCaseTestData.Cells(CorpusCaseTestData.LayoutBoundaries);
    public static TheoryData<string, string> RetentionCells => CorpusCaseTestData.Cells(CorpusCaseTestData.CollectorRetention);
    public static TheoryData<string, string> LifecycleCells => CorpusCaseTestData.Cells(CorpusCaseTestData.CollectorLifecycle);
    public static TheoryData<string, string> NativeCells => CorpusCaseTestData.Cells(CorpusCaseTestData.NativeOwnership);
    public static TheoryData<string, string> ReadonlyCells => CorpusCaseTestData.Cells(CorpusCaseTestData.ReadonlyCopies);
    public static TheoryData<string, string> ExactRootCells => CorpusCaseTestData.Cells(CorpusCaseTestData.ExactRootSlots);

    [Theory]
    [MemberData(nameof(UnsafeCells))]
    public void ValidUnsafeAndSpanOperationsPreserveBoundsAliasingAndFailureState(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(LayoutCells))]
    public void PackedExplicitNestedAndGenericLayoutsPreserveOffsetsValuesAndAliases(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(RetentionCells))]
    public void RealCollectionPreservesLiveRootsInteriorReferencesAndOwnedHandles(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(LifecycleCells))]
    public void RealCollectionClearsDeadWeakReferencesAndHonorsFinalizerLifecycle(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(NativeCells))]
    public void RealNativeAllocationsPreserveAlignmentContentsAndOwnership(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(ReadonlyCells))]
    public void ReadonlyAccessDefensivelyCopiesWhileWritableReferencesMutateTheirOwners(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(ExactRootCells))]
    public void RealCollectionDistinguishesManagedReferenceSlotsFromOpaqueAddressBits(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
