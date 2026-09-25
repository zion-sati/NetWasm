namespace NetWasm.Compiler.Tests.Correctness;

public sealed class OracleComparerTests
{
    private readonly OracleComparer _comparer = new();

    [Fact]
    public void AcceptsEquivalentValuesAndManagedExceptions()
    {
        var value = new OracleObservation(
            OracleObservationKind.Value, 42, null, 7);
        var exception = new OracleObservation(
            OracleObservationKind.ManagedException,
            null,
            "System.DivideByZeroException",
            9);

        Assert.True(_comparer.Compare(value, value).Equivalent);
        Assert.True(_comparer.Compare(exception, exception).Equivalent);
    }

    [Theory]
    [InlineData((int)OracleObservationKind.Trap, 42, null, 7)]
    [InlineData((int)OracleObservationKind.ManagedException, 42, null, 7)]
    [InlineData((int)OracleObservationKind.Value, 41, null, 7)]
    [InlineData((int)OracleObservationKind.Value, 42, "System.Exception", 7)]
    [InlineData((int)OracleObservationKind.Value, 42, null, 8)]
    public void RejectsEveryObservableMismatch(
        int kind,
        int? value,
        string? exceptionType,
        int trace)
    {
        var desktop = new OracleObservation(
            OracleObservationKind.Value, 42, null, 7);
        var netWasm = new OracleObservation(
            (OracleObservationKind)kind, value, exceptionType, trace)
        {
            Detail = "trap detail",
        };

        Assert.False(_comparer.Compare(desktop, netWasm).Equivalent);
    }

    [Fact]
    public void RejectsNullObservations()
    {
        var observation = new OracleObservation(
            OracleObservationKind.Value, 42, null, 0);
        Assert.Throws<ArgumentNullException>(() =>
            _comparer.Compare(null!, observation));
        Assert.Throws<ArgumentNullException>(() =>
            _comparer.Compare(observation, null!));
    }

    [Fact]
    public void ReportsTheFirstDifferingTypedTraceRecord()
    {
        var desktop = new OracleObservation(
            OracleObservationKind.Value, 42, null, 0)
        {
            TraceRecords =
            [
                new(TraceRecordKind.Mark, 1, 0),
                new(TraceRecordKind.Int64, 2, 42),
            ],
        };
        var netWasm = desktop with
        {
            TraceRecords =
            [
                new(TraceRecordKind.Mark, 1, 0),
                new(TraceRecordKind.Int64, 2, 43),
            ],
        };

        var comparison = _comparer.Compare(desktop, netWasm);

        Assert.False(comparison.Equivalent);
        Assert.Contains("event 1", comparison.Message);
        Assert.Contains("Payload = 42", comparison.Message);
        Assert.Contains("Payload = 43", comparison.Message);
    }

    [Theory]
    [InlineData((int)TraceRecordKind.Float32Bits, 1065353216L, 2143289344L)]
    [InlineData((int)TraceRecordKind.Float32Bits, 0L, 2147483648L)]
    [InlineData((int)TraceRecordKind.Float64Bits, 4607182418800017408L, 9221120237041090560L)]
    [InlineData((int)TraceRecordKind.Float64Bits, 0L, long.MinValue)]
    public void RejectsChangedFloatingClassOrZeroSignEvenWhenResultAndChecksumAgree(int kind, long expected, long actual)
    {
        var desktop = new OracleObservation(OracleObservationKind.Value, 100, null, 0)
        {
            TraceRecords = [new((TraceRecordKind)kind, 1, expected)],
        };
        var netWasm = desktop with { TraceRecords = [new((TraceRecordKind)kind, 1, actual)] };

        Assert.False(_comparer.Compare(desktop, netWasm).Equivalent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsReorderedOrMissingEventsEvenWhenResultAndChecksumAgree(bool omitted)
    {
        var desktop = new OracleObservation(OracleObservationKind.Value, 100, null, 0)
        {
            TraceRecords = [new(TraceRecordKind.Mark, 1, 0), new(TraceRecordKind.Mark, 2, 0)],
        };
        var netWasm = desktop with
        {
            TraceRecords = omitted
                ? [desktop.TraceRecords[0]]
                : [desktop.TraceRecords[1], desktop.TraceRecords[0]],
        };

        Assert.False(_comparer.Compare(desktop, netWasm).Equivalent);
    }

    [Theory]
    [InlineData((int)OracleObservationKind.TimedOut)]
    [InlineData((int)OracleObservationKind.CompileRejected)]
    public void NeverAcceptsTimeoutOrUnexpectedCompileRejection(int kind)
    {
        var observation = new OracleObservation(
            (OracleObservationKind)kind, null, null, 0)
        {
            TraceRecords = [],
        };

        Assert.False(_comparer.Compare(observation, observation).Equivalent);
    }
}
