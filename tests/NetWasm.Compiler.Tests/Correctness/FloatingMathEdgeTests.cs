namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class FloatingMathEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> ExactCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingMathExact);
    public static TheoryData<string, string> RoundingCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingRounding);
    public static TheoryData<string, string> DigitCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingRoundingDigits);

    public static TheoryData<string, string> ArgumentCells => CorpusCaseTestData.Cells(CorpusCaseTestData.RoundingArguments);
    public static TheoryData<string, string> DomainCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingMathDomains);
    public static TheoryData<string, string> BinaryCells => CorpusCaseTestData.Cells(CorpusCaseTestData.BinaryMathEdges);
    public static TheoryData<string, string> AccuracyCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingMathAccuracy);
    public static TheoryData<string, string> FullRangeCells => CorpusCaseTestData.Cells(CorpusCaseTestData.ManagedMathFullRange);

    [Theory]
    [MemberData(nameof(FullRangeCells))]
    public void ManagedTranscendentalsPreserveFullRangeAndSpecialValues(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(DigitCells))]
    public void DigitRoundingPreservesFractionalTiesAndLargeValueBoundaries(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.LargeIntegralMathFRounding)]
    public void LargeIntegralMathFRoundingDesktopScalingArtifactIsNotRequired() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory]
    [MemberData(nameof(AccuracyCells))]
    public void FiniteFunctionsStayWithinDeclaredReferenceErrorBudget(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(BinaryCells))]
    public void BinaryFunctionsRespectSpecialValuePrecedenceAndQuadrants(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.PowFiniteExactBits)]
    public void PowFiniteDesktopExactBitsAreNotRequired() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory]
    [MemberData(nameof(DomainCells))]
    public void DomainEdgesPreserveNaNInfinityAndSignedZeroContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.TrigonometricNegativeZero)]
    public void TrigonometricNegativeZeroDesktopBitsAreNotRequired() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory]
    [MemberData(nameof(ArgumentCells))]
    public void RoundingArgumentsRespectNet10LimitsAndExceptionPrecedence(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.SpecialValueRoundingValidationOrder)]
    public void SpecialValueRoundingInvalidModePrecedenceIsNotRequired() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory]
    [MemberData(nameof(RoundingCells))]
    public void RoundingModesRespectHalfwayNeighboursAndSignedZero(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(ExactCells))]
    public void ExactMathOperationsRespectNaNZeroAndAdjacentValueContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
