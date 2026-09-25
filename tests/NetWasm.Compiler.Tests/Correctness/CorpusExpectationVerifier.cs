namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CorpusExpectationVerifier : ICorpusExpectationVerifier
{
    void ICorpusExpectationVerifier.Verify(
        CorpusFixture fixture,
        IReadOnlyDictionary<int, OracleObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(fixture.ExpectedReturnValues);
        ArgumentNullException.ThrowIfNull(fixture.ExpectedExceptionTypes);
        if (fixture.Inputs.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A corpus fixture must declare at least one input.", nameof(fixture));
        }

        if (fixture.ExpectedReturnValue.HasValue &&
            (!fixture.ExpectedReturnValues.IsEmpty || !fixture.ExpectedExceptionTypes.IsEmpty))
        {
            throw new ArgumentException("Choose a uniform or per-input expectation, not both.", nameof(fixture));
        }
        foreach (var input in fixture.ExpectedReturnValues.Keys.Concat(fixture.ExpectedExceptionTypes.Keys))
        {
            if (!fixture.Inputs.Contains(input))
            {
                throw new ArgumentException("An expectation names an undeclared input.", nameof(fixture));
            }
        }
        foreach (var (input, exceptionType) in fixture.ExpectedExceptionTypes)
        {
            if (string.IsNullOrWhiteSpace(exceptionType) || fixture.ExpectedReturnValues.ContainsKey(input))
            {
                throw new ArgumentException("An exception expectation must name a type and cannot also expect a return value.", nameof(fixture));
            }
        }

        foreach (var input in fixture.Inputs)
        {
            if (!observations.TryGetValue(input, out var observation))
            {
                throw new InvalidOperationException(
                    $"{fixture.Name}: the oracle did not report input {input}.");
            }

            var expectedValue = fixture.ExpectedReturnValues.TryGetValue(input, out var perInput)
                ? perInput
                : fixture.ExpectedReturnValue;
            if (expectedValue is { } expected &&
                (observation.Kind != OracleObservationKind.Value || observation.Value != expected))
            {
                throw new InvalidOperationException(
                    $"{fixture.Name}: input {input} did not satisfy the independent " +
                    $"expected return value {expected}; observed {observation.Kind}/{observation.Value}.");
            }
            if (fixture.ExpectedExceptionTypes.TryGetValue(input, out var expectedException) &&
                (observation.Kind != OracleObservationKind.ManagedException ||
                 observation.ExceptionType != expectedException || observation.Value is not null))
            {
                throw new InvalidOperationException(
                    $"{fixture.Name}: input {input} did not satisfy the independent " +
                    $"expected managed exception {expectedException}.");
            }
        }
    }
}
