namespace NetWasm.Compiler.Tests.Correctness;

// Test composition only: production resolves the two commands independently.
internal static class CompiledCorpusTestFactory
{
    public static ICompiledCorpusRunner Create(
        IDesktopOracleRunner desktop,
        INetWasmOracleRunner netWasm,
        IOracleComparer comparer,
        ICompilerFailureArtifactWriter failures,
        IRandomCilCaseProgressReporter progress,
        IFrozenOracleEvidenceReader frozenEvidence,
        ICorpusExpectationVerifier expectations,
        ICorpusExecutionVerifier executions,
        ICorpusMatrixExpander matrices,
        CorpusHostIdentity hostIdentity,
        ICorpusSourceFingerprint sourceFingerprint,
        ILinkedCorpusOracleRunner? linked = null)
    {
        var selections = new CorpusCompilationCellsSelector(matrices);
        var comparisons = new CompiledCorpusComparisonRunner(
            netWasm, linked ?? RejectingLinkedCorpusOracleRunner.Instance,
            new RecordingReceiptDestinations(), new RecordingLinkedReceipts(), comparer, failures,
            progress, expectations, executions, selections);
        return new CompiledCorpusRunner(desktop, comparer, progress, frozenEvidence,
            expectations, selections, hostIdentity, sourceFingerprint, comparisons);
    }

    public static ILinkedCorpusOracleRunner RejectingLinked =>
        RejectingLinkedCorpusOracleRunner.Instance;

    internal sealed class RecordingReceiptDestinations : ILinkedCorpusReceiptDestinationValidator
    {
        public List<string> Directories { get; } = [];
        public Action? BeforeValidate { get; init; }

        public string Validate(string compilationDirectory)
        {
            BeforeValidate?.Invoke();
            Directories.Add(compilationDirectory);
            return "in-memory-receipts";
        }
    }

    internal sealed class RecordingLinkedReceipts : ILinkedCorpusQualificationReceiptWriter
    {
        public List<(CorpusCompilation Compilation, LinkedCorpusExecution Execution,
            System.Collections.Immutable.ImmutableDictionary<int, OracleObservation> Expected)> Writes
        { get; } = [];
        public Action? BeforeWrite { get; init; }

        public string Write(CorpusCompilation compilation, LinkedCorpusExecution execution,
            System.Collections.Immutable.ImmutableDictionary<int, OracleObservation> expected)
        {
            BeforeWrite?.Invoke();
            Writes.Add((compilation, execution, expected));
            return "in-memory-receipt";
        }
    }

    private sealed class RejectingLinkedCorpusOracleRunner : ILinkedCorpusOracleRunner
    {
        public static RejectingLinkedCorpusOracleRunner Instance { get; } = new();

        public LinkedCorpusExecution CompileBuildAndRun(
            CorpusCompilation compilation,
            CorpusMatrixCell cell,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("test did not expect linked corpus execution");
    }
}
