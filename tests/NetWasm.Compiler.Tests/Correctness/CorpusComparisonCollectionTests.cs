using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusComparisonCollectionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OptInComparesEveryInputIncludingPassingInputsAfterFailures(int wrongInputs)
    {
        var netWasm = new NetWasm(wrongInputs);
        var comparer = new Comparer();
        var failures = new Failures();
        var runner = Runner(netWasm, comparer, failures);
        var compilation = Compilation(true);

        if (wrongInputs == 0)
        {
            runner.RunAgainstOracle(compilation, Expected());
        }
        else
        {
            var error = Assert.Throws<AggregateException>(() => runner.RunAgainstOracle(compilation, Expected()));
            Assert.Contains($"3 observations, {wrongInputs} mismatches", error.Message);
            Assert.Equal(wrongInputs, error.InnerExceptions.Count);
            Assert.All(error.InnerExceptions, item => Assert.IsType<CorpusOracleMismatchException>(item));
            for (var index = 0; index < wrongInputs; index++)
            {
                Assert.Contains($"reproduction: bundle-{index + 1}", error.InnerExceptions[index].Message);
            }
        }

        Assert.Equal(3, comparer.Calls);
        Assert.Equal(Enumerable.Range(0, wrongInputs), failures.Writes.Select(item => item.Input));
        Assert.Equal([WasmTarget.Wasm32], netWasm.Targets);
    }

    [Fact]
    public void DefaultPreservesFirstMismatchExceptionAndStopsComparisons()
    {
        var comparer = new Comparer();
        var failures = new Failures();
        var runner = Runner(new NetWasm(2), comparer, failures);

        Assert.Throws<CorpusOracleMismatchException>(() => runner.RunAgainstOracle(Compilation(false), Expected()));

        Assert.Equal(1, comparer.Calls);
        Assert.Single(failures.Writes);
    }

    [Fact]
    public void CollectsBothTargetsAndFormsInInputThenTargetThenFormOrder()
    {
        var comparer = new Comparer();
        var failures = new Failures();
        var netWasm = new NetWasm(1);
        var runner = Runner(netWasm, comparer, failures, allCells: true);
        using var cancellation = new CancellationTokenSource();

        var error = Assert.Throws<AggregateException>(() =>
            runner.RunAgainstOracle(Compilation(true), Expected(), cancellation.Token));

        Assert.Contains("12 observations, 4 mismatches", error.Message);
        Assert.Equal(4, error.InnerExceptions.Count);
        Assert.Equal(12, comparer.Calls);
        Assert.Equal([WasmTarget.Wasm32, WasmTarget.Wasm64], netWasm.Targets);
        Assert.All(netWasm.Tokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Equal(
            [(0, WasmTarget.Wasm32, "direct.wasm"), (0, WasmTarget.Wasm32, "optimized.wasm"),
             (0, WasmTarget.Wasm64, "direct.wasm"), (0, WasmTarget.Wasm64, "optimized.wasm")],
            failures.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InfrastructureFailureAfterAMismatchIsNotAggregatedOrRetried(bool archiveFailure)
    {
        var failure = new InvalidOperationException("infrastructure failed");
        var comparer = new Comparer(archiveFailure ? null : failure);
        var failures = new Failures(archiveFailure ? failure : null);
        var runner = Runner(new NetWasm(3), comparer, failures);

        var error = Assert.Throws<InvalidOperationException>(() => runner.RunAgainstOracle(Compilation(true), Expected()));

        Assert.Same(failure, error);
        Assert.Equal(2, comparer.Calls);
        Assert.Equal(archiveFailure ? 2 : 1, failures.Attempts);
        Assert.Single(failures.Writes);
    }

    [Fact]
    public void CompilerFailureIsNotRetriedOrConvertedIntoASemanticMismatch()
    {
        var failure = new InvalidOperationException("compiler failed");
        var netWasm = new NetWasm(0, failure);
        var comparer = new Comparer();
        var failures = new Failures();
        var runner = Runner(netWasm, comparer, failures, allCells: true);

        var error = Assert.Throws<InvalidOperationException>(() => runner.RunAgainstOracle(Compilation(true), Expected()));

        Assert.Same(failure, error);
        Assert.Single(netWasm.Targets);
        Assert.Equal(0, comparer.Calls);
        Assert.Empty(failures.Writes);
    }

    [Fact]
    public void ExplicitLinkedCellsCompareEveryInputAgainstDesktopWithBackendIdentity()
    {
        var comparer = new Comparer();
        var failures = new Failures();
        var linked = new Linked(wrongInputs: 1);
        var netWasm = new NetWasm(0);
        var receipts = new CompiledCorpusTestFactory.RecordingLinkedReceipts
        {
            BeforeWrite = () => Assert.Equal(0, comparer.Calls),
        };
        var runner = Runner(netWasm, comparer, failures, linked: linked,
            linkedCells: true, receipts: receipts);
        using var cancellation = new CancellationTokenSource();
        var expected = Expected();

        var error = Assert.Throws<AggregateException>(() =>
            runner.RunAgainstOracle(Compilation(true), expected, cancellation.Token));

        Assert.Contains("6 observations, 2 mismatches", error.Message);
        Assert.Equal(6, comparer.Calls);
        Assert.Empty(netWasm.Targets);
        Assert.Equal(2, linked.Cells.Count);
        Assert.Equal(2, receipts.Writes.Count);
        Assert.Equal(linked.Cells.Select(cell => cell.Target),
            receipts.Writes.Select(write => write.Execution.Target));
        Assert.All(receipts.Writes, write =>
        {
            Assert.Same(expected, write.Expected);
            Assert.Equal("Collection", write.Compilation.Fixture.Name);
        });
        Assert.All(linked.Tokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Equal([CorpusWasmForm.Direct, CorpusWasmForm.Optimized],
            failures.Executions.Select(execution => execution.Form));
        Assert.All(failures.Executions, execution =>
            Assert.Equal(CorpusExecutionBackend.Linked, execution.Backend));
        Assert.Equal([WasmTarget.Wasm32, WasmTarget.Wasm64],
            failures.Executions.Select(execution => execution.Target));
    }

    [Fact]
    public void ReceiptFailureStopsBeforeComparisonsAndLaterLinkedCells()
    {
        var failure = new IOException("receipt failed");
        var comparer = new Comparer();
        var failures = new Failures();
        var linked = new Linked(0);
        var receipts = new CompiledCorpusTestFactory.RecordingLinkedReceipts
        {
            BeforeWrite = () => throw failure,
        };
        var runner = Runner(new NetWasm(0), comparer, failures, linked: linked,
            linkedCells: true, receipts: receipts);
        Assert.Same(failure, Assert.Throws<IOException>(() =>
            runner.RunAgainstOracle(Compilation(true), Expected())));
        Assert.Single(linked.Cells);
        Assert.Empty(receipts.Writes);
        Assert.Equal(0, comparer.Calls);
        Assert.Empty(failures.Writes);
    }

    [Fact]
    public void SimulatedCellsDoNotRequireLinkedReceiptDestination()
    {
        var receipts = new CompiledCorpusTestFactory.RecordingLinkedReceipts
        {
            BeforeWrite = () => throw new InvalidOperationException("unexpected linked receipt"),
        };
        var destinations = new CompiledCorpusTestFactory.RecordingReceiptDestinations
        {
            BeforeValidate = () => throw new InvalidOperationException("unexpected linked preflight"),
        };
        var comparer = new Comparer();
        Runner(new NetWasm(0), comparer, new Failures(), receipts: receipts, destinations: destinations)
            .RunAgainstOracle(Compilation(false), Expected());
        Assert.Equal(3, comparer.Calls);
        Assert.Empty(receipts.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedReceiptPreflightRunsOnceBeforeExecutionAndStopsOnFailure(bool fail)
    {
        var netWasm = new NetWasm(0);
        var linked = new Linked(0);
        var comparer = new Comparer();
        var failures = new Failures();
        var receipts = new CompiledCorpusTestFactory.RecordingLinkedReceipts();
        var failure = new InvalidOperationException("invalid receipt destination");
        var destinations = new CompiledCorpusTestFactory.RecordingReceiptDestinations
        {
            BeforeValidate = () =>
            {
                Assert.Empty(netWasm.Targets);
                Assert.Empty(linked.Cells);
                Assert.Empty(receipts.Writes);
                if (fail) throw failure;
            },
        };
        var runner = Runner(netWasm, comparer, failures, linked: linked, linkedCells: true,
            receipts: receipts, destinations: destinations);
        if (fail)
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                runner.RunAgainstOracle(Compilation(false), Expected())));
            Assert.Empty(linked.Cells);
            Assert.Empty(receipts.Writes);
            Assert.Equal(0, comparer.Calls);
        }
        else
        {
            runner.RunAgainstOracle(Compilation(false), Expected());
            Assert.Equal(["in-memory"], destinations.Directories);
            Assert.Equal(2, linked.Cells.Count);
            Assert.Equal(2, receipts.Writes.Count);
            Assert.Equal(6, comparer.Calls);
        }
        Assert.Empty(failures.Writes);
    }

    private static ICompiledCorpusComparisonRunner Runner(
        NetWasm netWasm,
        Comparer comparer,
        Failures failures,
        bool allCells = false,
        ILinkedCorpusOracleRunner? linked = null,
        bool linkedCells = false,
        ILinkedCorpusQualificationReceiptWriter? receipts = null,
        ILinkedCorpusReceiptDestinationValidator? destinations = null) =>
        Assert.IsAssignableFrom<ICompiledCorpusComparisonRunner>(new CompiledCorpusComparisonRunner(
            netWasm, linked ?? CompiledCorpusTestFactory.RejectingLinked,
            destinations ?? new CompiledCorpusTestFactory.RecordingReceiptDestinations(),
            receipts ?? new CompiledCorpusTestFactory.RecordingLinkedReceipts(), comparer, failures,
            new RandomCilCaseProgressReporter(TextWriter.Null), new CorpusExpectationVerifier(),
            new CorpusExecutionVerifier(), new Cells(allCells, linkedCells)));

    private static CorpusCompilation Compilation(bool reportAll)
    {
        var artifact = new CorpusArtifact("fixture.dll", "", "sha", "", "sdk", []);
        return new(new("Collection", "Collection", "source", [0, 1, 2])
        {
            ReportAllMismatches = reportAll,
            ExpectedReturnValue = 42,
        }, CilProfile.Release, artifact, artifact, "in-memory");
    }

    private static ImmutableDictionary<int, OracleObservation> Expected() =>
        Enumerable.Range(0, 3).ToImmutableDictionary(input => input, _ => new OracleObservation(OracleObservationKind.Value, 42, null, 0));

    private sealed class Cells(bool allCells, bool linkedCells = false) : ICorpusCompilationCellsSelector
    {
        public ImmutableArray<CorpusMatrixCell> Select(CorpusCompilation compilation) => linkedCells
            ? [new(CilProfile.Release, WasmTarget.Wasm32, CorpusWasmForm.Direct, CorpusExecutionBackend.Linked),
               new(CilProfile.Release, WasmTarget.Wasm64, CorpusWasmForm.Optimized, CorpusExecutionBackend.Linked)]
            : allCells
                ? [new(CilProfile.Release, WasmTarget.Wasm32, CorpusWasmForm.Direct), new(CilProfile.Release, WasmTarget.Wasm32, CorpusWasmForm.Optimized),
                   new(CilProfile.Release, WasmTarget.Wasm64, CorpusWasmForm.Direct), new(CilProfile.Release, WasmTarget.Wasm64, CorpusWasmForm.Optimized)]
                : [new(CilProfile.Release, WasmTarget.Wasm32, CorpusWasmForm.Direct)];
    }

    private sealed class Linked(int wrongInputs) : ILinkedCorpusOracleRunner
    {
        public List<CorpusMatrixCell> Cells { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public LinkedCorpusExecution CompileBuildAndRun(
            CorpusCompilation compilation,
            CorpusMatrixCell cell,
            CancellationToken cancellationToken = default)
        {
            Cells.Add(cell);
            Tokens.Add(cancellationToken);
            var observations = Enumerable.Range(0, 3).ToImmutableDictionary(input => input,
                input => new OracleObservation(OracleObservationKind.Value,
                    input < wrongInputs ? 43 : 42, null, 0));
            return new(observations, cell.Target, cell.Form,
                "application.wasm", "application-sha", "layout.json", "layout-sha",
                "manifest.json", "manifest-sha", "linked.wasm", "linked-sha", []);
        }
    }

    private sealed class NetWasm(int wrongInputs, Exception? failure = null) : INetWasmOracleRunner
    {
        public List<WasmTarget> Targets { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public NetWasmExecution CompileAndRun(CorpusCompilation compilation, WasmTarget target, CancellationToken cancellationToken = default)
        {
            Targets.Add(target);
            Tokens.Add(cancellationToken);
            if (failure is not null) throw failure;
            var observations = Enumerable.Range(0, 3).ToImmutableDictionary(input => input,
                input => new OracleObservation(OracleObservationKind.Value, input < wrongInputs ? 43 : 42, null, 0));
            return new(observations, null, "direct.wasm", "direct-sha", target, true)
            {
                OptimizedObservations = observations,
                OptimizedModulePath = "optimized.wasm",
                OptimizedModuleSha256 = "optimized-sha",
            };
        }
    }

    private sealed class Comparer(Exception? failure = null) : IOracleComparer
    {
        public int Calls { get; private set; }
        public OracleComparison Compare(OracleObservation desktop, OracleObservation netWasm)
        {
            Calls++;
            if (Calls == 2 && failure is not null) throw failure;
            return new(desktop.Value == netWasm.Value, "value mismatch");
        }
    }

    private sealed class Failures(Exception? failure = null) : ICompilerFailureArtifactWriter
    {
        public int Attempts { get; private set; }
        public List<(int Input, WasmTarget Target, string Module)> Writes { get; } = [];
        public List<NetWasmExecution> Executions { get; } = [];
        public string Write(CorpusCompilation compilation, int input, OracleObservation desktop, OracleObservation netWasm,
            NetWasmExecution execution, string reason)
        {
            Attempts++;
            if (Attempts == 2 && failure is not null) throw failure;
            Writes.Add((input, execution.Target, execution.ModulePath));
            Executions.Add(execution);
            return $"bundle-{Writes.Count}";
        }
    }
}
