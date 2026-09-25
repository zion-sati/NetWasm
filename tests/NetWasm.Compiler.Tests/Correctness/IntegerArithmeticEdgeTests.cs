namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class IntegerArithmeticEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FixedIntegerArithmetic);
    public static TheoryData<string, string> ShiftUnaryCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FixedIntegerShiftsUnary);
    public static TheoryData<string, string> NativeCells => CorpusCaseTestData.Cells(CorpusCaseTestData.NativeArithmetic);
    public static TheoryData<string, string> NativeShiftUnaryCells => CorpusCaseTestData.Cells(CorpusCaseTestData.NativeShiftsUnary);

    [Theory]
    [MemberData(nameof(NativeShiftUnaryCells))]
    public void NativeShiftsAndUnaryOperationsUseTargetWidthResultsAndState(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(NativeCells))]
    public void NativeArithmeticUsesTargetWidthValueAndExceptionContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.UncheckedRemainder)]
    public void NativeUncheckedMinimumRemainderDesktopChoiceIsNotRequired() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory]
    [MemberData(nameof(ShiftUnaryCells))]
    public void ShiftsAndUnaryOperationsPreserveMaskedCountsResultsAndState(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void FixedWidthOperationsRespectWrappingOverflowAndDivisionContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Active(caseId, cell), cell);

    [Fact(Skip = KnownNonBugSkipReasons.UncheckedRemainder)]
    public void FixedUncheckedMinimumRemainderDesktopChoiceIsNotRequired() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");
}
