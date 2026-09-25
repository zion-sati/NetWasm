namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class WideIntegerEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> ArithmeticCells => CorpusCaseTestData.Cells(CorpusCaseTestData.WideArithmetic);
    public static TheoryData<string, string> ConversionCells => CorpusCaseTestData.Cells(CorpusCaseTestData.WideConversions);
    public static TheoryData<string, string> FloatingCells => CorpusCaseTestData.Cells(CorpusCaseTestData.WideFloating);
    public static TheoryData<string, string> FromFloatingCells => CorpusCaseTestData.Cells(CorpusCaseTestData.FloatingWide);
    public static TheoryData<string, string> GenericFromFloatingCells => CorpusCaseTestData.Cells(CorpusCaseTestData.GenericFloatingWide);
    public static TheoryData<string, string> GenericToFloatingCells => CorpusCaseTestData.Cells(CorpusCaseTestData.GenericWideFloating);

    [Theory]
    [MemberData(nameof(GenericToFloatingCells))]
    public void GenericWideInputsRespectFloatingRoundingForEveryPolicy(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(GenericFromFloatingCells))]
    public void GenericFloatingInputsRespectCheckedSaturatingAndTruncatingPolicies(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(FromFloatingCells))]
    public void FloatingInputsRespectWideTruncationSaturationAndCheckedRanges(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(FloatingCells))]
    public void FloatingCastsRespectNet10RoundingAndOverflowBoundaries(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(ConversionCells))]
    public void CastsAndGenericConversionsPreserveDistinctOverflowPolicies(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(ArithmeticCells))]
    public void ArithmeticAndShiftsRespect128BitValueAndExceptionContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
