namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class HalfEdgeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void EveryHalfSubnormalWidensExactlyWithItsSign() => Run(new(
        "HalfSubnormalWidening", "NetWasm.Correctness.HalfSubnormalWidening",
        """
        using System;
        namespace NetWasm.Correctness.HalfSubnormalWidening;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                for (var sign = 0; sign < 2; sign++)
                for (var fraction = 0; fraction < 1024; fraction++)
                {
                    var bits = (ushort)((sign << 15) | fraction);
                    var value = BitConverter.UInt16BitsToHalf(bits);
                    var expected = fraction * (1.0 / 16777216.0);
                    if (sign != 0) expected = -expected;
                    if (BitConverter.DoubleToInt64Bits((double)value) != BitConverter.DoubleToInt64Bits(expected)) return -1;
                    if (BitConverter.SingleToInt32Bits((float)value) != BitConverter.SingleToInt32Bits((float)expected)) return -2;
                }
                return 42;
            }
            public static int Trace() => 0;
        }
        """, [0])
    {
        ExpectedReturnValue = 42,
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
    });

    public static TheoryData<string, string> WideningCells => CorpusCaseTestData.Cells(CorpusCaseTestData.HalfWidening);
    public static TheoryData<string, string> NarrowingCells => CorpusCaseTestData.Cells(CorpusCaseTestData.HalfNarrowing);
    public static TheoryData<string, string> ClassificationCells => CorpusCaseTestData.Cells(CorpusCaseTestData.HalfClassification);

    [Theory]
    [MemberData(nameof(ClassificationCells))]
    public void ClassificationAndZeroComparisonsExecuteIndependently(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(NarrowingCells))]
    public void NarrowingRespectsHalfwayRoundingAndExceptionalValues(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);

    [Theory]
    [MemberData(nameof(WideningCells))]
    public void WideningRoundTripsAndClassificationPreserveBinary16Contracts(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
