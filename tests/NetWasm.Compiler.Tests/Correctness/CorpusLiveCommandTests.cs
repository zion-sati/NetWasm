using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusLiveCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatedOracleIsDelegatedWithOriginalCompilationAndCancellation(bool frozen)
    {
        var compilation = Compilation(frozen);
        using var cancellation = new CancellationTokenSource();
        var calls = new List<string>();
        var live = Observations(42);
        var saved = Observations(42);
        var runner = Create(compilation, calls, live, saved, token: cancellation.Token);

        runner.Run(compilation, cancellation.Token);

        Assert.Equal(frozen
            ? ["select", "progress", "desktop", "expectations", "fingerprint", "frozen", "compare", "delegate-frozen"]
            : ["select", "progress", "desktop", "expectations", "delegate-live"], calls);
    }

    [Theory]
    [InlineData("select")]
    [InlineData("progress")]
    [InlineData("desktop")]
    [InlineData("expectations")]
    [InlineData("fingerprint")]
    [InlineData("frozen")]
    [InlineData("compare")]
    [InlineData("delegate-frozen")]
    public void DependencyFailureStopsImmediatelyAndPreservesOriginalException(string stage)
    {
        var compilation = Compilation(true);
        var calls = new List<string>();
        var failure = new InvalidOperationException("injected dependency failure");
        var runner = Create(compilation, calls, Observations(42), Observations(42), stage, failure);

        var error = Assert.Throws<InvalidOperationException>(() => runner.Run(compilation));

        Assert.Same(failure, error);
        string[] sequence = ["select", "progress", "desktop", "expectations", "fingerprint", "frozen", "compare", "delegate-frozen"];
        Assert.Equal(sequence.Take(Array.IndexOf(sequence, stage) + 1), calls);
    }

    private static ICompiledCorpusRunner Create(CorpusCompilation compilation,
        List<string> calls, ImmutableDictionary<int, OracleObservation> live,
        ImmutableDictionary<int, OracleObservation> frozen, string? failedStage = null, Exception? failure = null,
        CancellationToken token = default)
    {
        void Record(string stage)
        {
            calls.Add(stage);
            if (stage == failedStage) throw failure!;
        }

        return Assert.IsAssignableFrom<ICompiledCorpusRunner>(new CompiledCorpusRunner(
            new Desktop((actual, actualToken) =>
            {
                Assert.Same(compilation, actual);
                Assert.Equal(token, actualToken);
                Record("desktop");
                return live;
            }),
            new Comparer((expected, actual) =>
            {
                Assert.Same(live[0], expected);
                Assert.Same(frozen[0], actual);
                Record("compare");
                return new(true, string.Empty);
            }),
            new Progress((name, stage) =>
            {
                Assert.Equal(compilation.Fixture.Name, name);
                Assert.Equal(RandomCilCaseStage.DesktopOracle, stage);
                Record("progress");
            }),
            new Frozen(request =>
            {
                Assert.Equal("frozen.json", request.RelativePath);
                Assert.Equal("source-sha", request.SourceSha256);
                Record("frozen");
                return frozen;
            }),
            new Expectations((fixture, expected) =>
            {
                Assert.Same(compilation.Fixture, fixture);
                Assert.Same(live, expected);
                Record("expectations");
            }),
            new Selections(actual =>
            {
                Assert.Same(compilation, actual);
                Record("select");
                return default;
            }),
            CorrectnessTestAssets.HostIdentity,
            new Fingerprint(fixture =>
            {
                Assert.Same(compilation.Fixture, fixture);
                Record("fingerprint");
                return "source-sha";
            }),
            new Comparison((actual, expected, actualToken) =>
            {
                Assert.Same(compilation, actual);
                Assert.Equal(token, actualToken);
                var useFrozen = compilation.Fixture.FrozenOracleEvidencePath is not null;
                Assert.Same(useFrozen ? frozen : live, expected);
                Record(useFrozen ? "delegate-frozen" : "delegate-live");
            })));
    }

    private static CorpusCompilation Compilation(bool frozen)
    {
        var artifact = new CorpusArtifact("fixture.dll", "", "assembly-sha", "", "sdk", []);
        return new(new("LiveCommand", "LiveCommand", "source", [0])
        {
            FrozenOracleEvidencePath = frozen ? "frozen.json" : null,
        }, CilProfile.Release, artifact, artifact, "in-memory");
    }

    private static ImmutableDictionary<int, OracleObservation> Observations(int value) =>
        ImmutableDictionary<int, OracleObservation>.Empty.Add(0, new(OracleObservationKind.Value, value, null, 0));

    private sealed class Desktop(Func<CorpusCompilation, CancellationToken, ImmutableDictionary<int, OracleObservation>> run) : IDesktopOracleRunner
    {
        public ImmutableDictionary<int, OracleObservation> Run(CorpusCompilation compilation, CancellationToken cancellationToken = default) =>
            run(compilation, cancellationToken);
    }

    private sealed class Comparer(Func<OracleObservation, OracleObservation, OracleComparison> compare) : IOracleComparer
    {
        public OracleComparison Compare(OracleObservation desktop, OracleObservation netWasm) => compare(desktop, netWasm);
    }

    private sealed class Progress(Action<string, RandomCilCaseStage> report) : IRandomCilCaseProgressReporter
    {
        public void Report(string fixtureId, RandomCilCaseStage stage) => report(fixtureId, stage);
    }

    private sealed class Frozen(Func<FrozenOracleRequest, ImmutableDictionary<int, OracleObservation>> read) : IFrozenOracleEvidenceReader
    {
        public ImmutableDictionary<int, OracleObservation> Read(FrozenOracleRequest request) => read(request);
    }

    private sealed class Expectations(Action<CorpusFixture, IReadOnlyDictionary<int, OracleObservation>> verify) : ICorpusExpectationVerifier
    {
        public void Verify(CorpusFixture fixture, IReadOnlyDictionary<int, OracleObservation> observations) => verify(fixture, observations);
    }

    private sealed class Selections(Func<CorpusCompilation, ImmutableArray<CorpusMatrixCell>> select) : ICorpusCompilationCellsSelector
    {
        public ImmutableArray<CorpusMatrixCell> Select(CorpusCompilation compilation) => select(compilation);
    }

    private sealed class Fingerprint(Func<CorpusFixture, string> compute) : ICorpusSourceFingerprint
    {
        public string Compute(CorpusFixture fixture) => compute(fixture);
    }

    private sealed class Comparison(Action<CorpusCompilation, ImmutableDictionary<int, OracleObservation>, CancellationToken> run) : ICompiledCorpusComparisonRunner
    {
        public void RunAgainstOracle(CorpusCompilation compilation, ImmutableDictionary<int, OracleObservation> expected,
            CancellationToken cancellationToken = default) => run(compilation, expected, cancellationToken);
    }
}
