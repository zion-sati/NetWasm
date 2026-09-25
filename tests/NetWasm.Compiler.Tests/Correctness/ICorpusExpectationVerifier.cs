namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusExpectationVerifier
{
    void Verify(
        CorpusFixture fixture,
        IReadOnlyDictionary<int, OracleObservation> observations);
}
