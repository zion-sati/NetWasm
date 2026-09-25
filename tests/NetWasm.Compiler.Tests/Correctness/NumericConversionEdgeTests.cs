namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class NumericConversionEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> PrecisionCells => CorpusCaseTestData.Cells(CorpusCaseTestData.NumericPrecision);
    public static TheoryData<string, string> NonFiniteCells => CorpusCaseTestData.Cells(CorpusCaseTestData.CheckedNonFinite);
    public static TheoryData<string, string> FiniteCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FiniteFloating);
    public static TheoryData<string, string> UnsignedCells => CorpusCaseTestData.Cells(CorpusCaseTestData.UnsignedPrecision);
    public static TheoryData<string, string> NarrowingCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingNarrowing);
    public static TheoryData<string, string> IntegerCells => CorpusCaseTestData.Cells(CorpusCaseTestData.IntegerCasts);
    public static TheoryData<string, string> NativeCells => CorpusCaseTestData.Cells(CorpusCaseTestData.NativeCasts);
    public static TheoryData<string, string> FloatingNativeCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingNative);
    public static TheoryData<string, string> NativeFloatingCells => CorpusCaseTestData.Cells(CorpusCaseTestData.NativeFloating);
    public static TheoryData<string, string> ConvertCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingConvertInteger);
    public static TheoryData<string, string> SmallIntegerCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingSmallInteger);

    [Theory]
    [MemberData(nameof(SmallIntegerCells))]
    public void SmallIntegerCastsTruncateBeforeCheckingDestinationRange(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(ConvertCells))]
    public void ConvertRoundsToEvenBeforeCheckingIntegerRanges(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(NativeFloatingCells))]
    public void NativeInputsRespectWidthSpecificFloatingRounding(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.NativeUnsignedSinglePrecision)]
    public void NativeUnsignedToSingleDesktopExactBitsAreNotRequired() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory]
    [MemberData(nameof(FloatingNativeCells))]
    public void FloatingInputsRespectNativeWidthTruncationAndOverflow(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(NativeCells))]
    public void NativeCastsRespectTargetWidthAndCheckedOverflow(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(IntegerCells))]
    public void IntegerCastsRespectSignednessWidthAndOverflow(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(PrecisionCells))]
    public void ConversionsPreserveSpecifiedPrecisionAndRoundTripResults(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(NonFiniteCells))]
    public void CheckedNonFiniteConversionsThrowManagedOverflow(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(FiniteCells))]
    public void FiniteFloatingConversionsRespectTruncationAndRange(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(UnsignedCells))]
    public void UnsignedConversionsPreserveRoundingAndCheckedRoundTripBounds(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.UnsignedSinglePrecision)]
    public void UnsignedToSingleDesktopExactBitsAreNotRequired() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory]
    [MemberData(nameof(NarrowingCells))]
    public void FloatingNarrowingPreservesRoundingClassificationAndZeroSigns(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
