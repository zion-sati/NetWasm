using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusExpectationVerifierTests
{
    private readonly ICorpusExpectationVerifier _verifier = new CorpusExpectationVerifier();

    [Fact]
    public void AcceptsEveryDeclaredInputWithTheExpectedReturnValue() =>
        _verifier.Verify(CreateFixture(), Observations(100));

    [Fact]
    public void DifferentialOnlyFixturesDoNotAcquireAnImplicitSuccessSentinel() =>
        _verifier.Verify(CreateFixture() with { ExpectedReturnValue = null }, Observations(3));

    [Fact]
    public void RejectsAMatchingFailureSentinel()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(CreateFixture(), Observations(3)));

        Assert.Contains("independent expected return value 100", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RejectsNonValueOutcomesEvenWithTheExpectedNumericPayload(int kind)
    {
        var observations = Observations(100).SetItem(
            2, new((OracleObservationKind)kind, 100, null, 0));

        Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(CreateFixture(), observations));
    }

    [Fact]
    public void RejectsAMissingValuePayload() =>
        Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(CreateFixture(), Observations(null)));

    [Theory]
    [InlineData(null)]
    [InlineData(100)]
    public void RejectsMissingInputsEvenWithoutAnIndependentValueExpectation(int? expected) =>
        Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(CreateFixture() with { ExpectedReturnValue = expected },
                Observations(100).Remove(2)));

    [Fact]
    public void RejectsAnEmptyCaseInsteadOfVacuouslyPassing() =>
        Assert.Throws<ArgumentException>(() =>
            _verifier.Verify(CreateFixture() with { Inputs = [] }, Observations(100)));

    [Fact]
    public void RejectsDefaultInputs() =>
        Assert.Throws<ArgumentException>(() =>
            _verifier.Verify(CreateFixture() with { Inputs = default }, Observations(100)));

    [Fact]
    public void RejectsNullFixture() =>
        Assert.Throws<ArgumentNullException>(() => _verifier.Verify(null!, Observations(100)));

    [Fact]
    public void RejectsNullObservations() =>
        Assert.Throws<ArgumentNullException>(() => _verifier.Verify(CreateFixture(), null!));

    [Fact]
    public void AcceptsDifferentIndependentValuesForDifferentInputs() =>
        _verifier.Verify(CreateFixture() with
        {
            ExpectedReturnValue = null,
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(1, 100).Add(2, 7),
        }, Observations(100).SetItem(2, new(OracleObservationKind.Value, 7, null, 0)));

    [Fact]
    public void UnpinnedInputsRemainDifferentialObservations() =>
        _verifier.Verify(CreateFixture() with
        {
            ExpectedReturnValue = null,
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(1, 100),
        }, Observations(100).SetItem(2, new(OracleObservationKind.ManagedException, null, "ExpectedException", 0)));

    [Fact]
    public void RejectsFailureSentinelForAnIndividuallyPinnedInput() =>
        Assert.Throws<InvalidOperationException>(() => _verifier.Verify(CreateFixture() with
        {
            ExpectedReturnValue = null,
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(2, 100),
        }, Observations(3)));

    [Fact]
    public void RejectsAnExpectationForAnUndeclaredInput() =>
        Assert.Throws<ArgumentException>(() => _verifier.Verify(CreateFixture() with
        {
            ExpectedReturnValue = null,
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(3, 100),
        }, Observations(100)));

    [Fact]
    public void RejectsAmbiguousUniformAndPerInputExpectations() =>
        Assert.Throws<ArgumentException>(() => _verifier.Verify(CreateFixture() with
        {
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(1, 100),
        }, Observations(100)));

    [Fact]
    public void RejectsNullPerInputExpectations() =>
        Assert.Throws<ArgumentNullException>(() => _verifier.Verify(CreateFixture() with
        {
            ExpectedReturnValues = null!,
        }, Observations(100)));

    [Fact]
    public void AcceptsIndependentExceptionAndValueContractsForDifferentInputs() =>
        _verifier.Verify(ExceptionFixture() with
        {
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(1, 100),
        }, Observations(100).SetItem(2, new(OracleObservationKind.ManagedException, null, "System.ArgumentException", 0)));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ExpectedExceptionCannotBeReplacedByAnotherOutcomeKind(int kind) =>
        Assert.Throws<InvalidOperationException>(() => _verifier.Verify(ExceptionFixture(),
            Observations(100).SetItem(2, new((OracleObservationKind)kind, null, "System.ArgumentException", 0))));

    [Theory]
    [InlineData(null)]
    [InlineData("System.InvalidOperationException")]
    public void RejectsTheWrongManagedExceptionType(string? exceptionType) =>
        Assert.Throws<InvalidOperationException>(() => _verifier.Verify(ExceptionFixture(),
            Observations(100).SetItem(2, new(OracleObservationKind.ManagedException, null, exceptionType, 0))));

    [Fact]
    public void RejectsAnExceptionObservationWithAValuePayload() =>
        Assert.Throws<InvalidOperationException>(() => _verifier.Verify(ExceptionFixture(),
            Observations(100).SetItem(2, new(OracleObservationKind.ManagedException, 100, "System.ArgumentException", 0))));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ExceptionExpectationRequiresATypeName(string? exceptionType) =>
        Assert.Throws<ArgumentException>(() => _verifier.Verify(ExceptionFixture() with
        {
            ExpectedExceptionTypes = ImmutableDictionary<int, string>.Empty.Add(2, exceptionType!),
        }, Observations(100)));

    [Fact]
    public void RejectsOverlappingExceptionAndValueContracts() =>
        Assert.Throws<ArgumentException>(() => _verifier.Verify(ExceptionFixture() with
        {
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(2, 100),
        }, Observations(100)));

    [Fact]
    public void RejectsUniformSuccessCombinedWithAnExceptionContract() =>
        Assert.Throws<ArgumentException>(() => _verifier.Verify(ExceptionFixture() with
        {
            ExpectedReturnValue = 100,
        }, Observations(100)));

    [Fact]
    public void RejectsAnExceptionContractForAnUnknownInput() =>
        Assert.Throws<ArgumentException>(() => _verifier.Verify(ExceptionFixture() with
        {
            ExpectedExceptionTypes = ImmutableDictionary<int, string>.Empty.Add(3, "System.ArgumentException"),
        }, Observations(100)));

    [Fact]
    public void RejectsNullExceptionContracts() =>
        Assert.Throws<ArgumentNullException>(() => _verifier.Verify(CreateFixture() with
        {
            ExpectedExceptionTypes = null!,
        }, Observations(100)));

    private static CorpusFixture ExceptionFixture() => CreateFixture() with
    {
        ExpectedReturnValue = null,
        ExpectedExceptionTypes = ImmutableDictionary<int, string>.Empty.Add(2, "System.ArgumentException"),
    };

    private static CorpusFixture CreateFixture() => new("Expectation", "Unused", "", [1, 2])
    {
        ExpectedReturnValue = 100,
    };

    private static ImmutableDictionary<int, OracleObservation> Observations(int? value) =>
        ImmutableArray.Create(1, 2).ToImmutableDictionary(input => input,
            _ => new OracleObservation(OracleObservationKind.Value, value, null, 0));
}
