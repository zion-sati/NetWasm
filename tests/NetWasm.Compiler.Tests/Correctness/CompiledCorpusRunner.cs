namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CompiledCorpusRunner(
    IDesktopOracleRunner desktop,
    IOracleComparer comparer,
    IRandomCilCaseProgressReporter progress,
    IFrozenOracleEvidenceReader frozenEvidence,
    ICorpusExpectationVerifier expectations,
    ICorpusCompilationCellsSelector selections,
    CorpusHostIdentity hostIdentity,
    ICorpusSourceFingerprint sourceFingerprint,
    ICompiledCorpusComparisonRunner comparisons) : ICompiledCorpusRunner
{
    public void Run(
        CorpusCompilation compilation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        selections.Select(compilation);
        progress.Report(compilation.Fixture.Name, RandomCilCaseStage.DesktopOracle);
        var liveExpected = desktop.Run(compilation, cancellationToken);
        expectations.Verify(compilation.Fixture, liveExpected);
        var expected = liveExpected;
        if (compilation.Fixture.FrozenOracleEvidencePath is { } evidencePath)
        {
            var frozenExpected = frozenEvidence.Read(new FrozenOracleRequest(
                evidencePath,
                compilation.Fixture.Name,
                compilation.Profile,
                compilation.Fixture.Inputs,
                sourceFingerprint.Compute(compilation.Fixture),
                compilation.Desktop.AssemblySha256,
                compilation.Desktop.CompilerVersion,
                hostIdentity.TargetFramework ?? string.Empty,
                hostIdentity.RuntimeVersion,
                hostIdentity.FrameworkDescription,
                hostIdentity.ProcessArchitecture));
            foreach (var input in compilation.Fixture.Inputs)
            {
                var comparison = comparer.Compare(
                    liveExpected[input],
                    frozenExpected[input]);
                if (!comparison.Equivalent)
                {
                    throw new InvalidOperationException(
                        $"frozen desktop oracle does not match the current desktop oracle " +
                        $"for input {input}: live={liveExpected[input].Kind}/" +
                        $"{liveExpected[input].Value}, frozen={frozenExpected[input].Kind}/" +
                        $"{frozenExpected[input].Value}, liveTrace=" +
                        $"{liveExpected[input].TraceRecords.Length}, frozenTrace=" +
                        $"{frozenExpected[input].TraceRecords.Length}, liveException=" +
                        $"{liveExpected[input].ExceptionType ?? "none"}, frozenException=" +
                        $"{frozenExpected[input].ExceptionType ?? "none"}");
                }
            }
            expected = frozenExpected;
        }
        comparisons.RunAgainstOracle(compilation, expected, cancellationToken);
    }
}
