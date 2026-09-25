namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class DecimalEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.DecimalEdges);
    public static TheoryData<string, string> ConversionCells => CorpusCaseTestData.Cells(CorpusCaseTestData.DecimalConversions);
    public static TheoryData<string, string> ArithmeticCells => CorpusCaseTestData.Cells(CorpusCaseTestData.DecimalArithmetic);
    public static TheoryData<string, string> BinaryCells => CorpusCaseTestData.Cells(CorpusCaseTestData.DecimalBinary);
    public static TheoryData<string, string> FloatingInputCells => CorpusCaseTestData.Cells(CorpusCaseTestData.BinaryDecimal);

    [Theory]
    [MemberData(nameof(FloatingInputCells))]
    public void FloatingInputsRespectDecimalRangeUnderflowAndPrecision(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(BinaryCells))]
    public void BinaryConversionsRespectExactRoundingAndSignedZero(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(ArithmeticCells))]
    public void ArithmeticRespectsRoundingOverflowAndDivisionContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(ConversionCells))]
    public void IntegerConversionsDistinguishTruncationRoundingAndOverflow(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void RepresentationAndRoundingRespectTheirDistinctContracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
