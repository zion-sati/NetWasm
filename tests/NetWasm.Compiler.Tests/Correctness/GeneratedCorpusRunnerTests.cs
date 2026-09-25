using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GeneratedCorpusRunnerTests
{
    [Fact]
    public void RunDelegatesGeneratedFixtureToDifferentialRunner()
    {
        var differential = new RecordingDifferentialRunner();
        var subject = new GeneratedCorpusRunner(
            differential,
            new UnexpectedFailureWriter(),
            new SourceFailureReducer(TimeProvider.System));
        var generatedCase = Case();

        subject.Run(generatedCase);

        Assert.Same(generatedCase.Fixture, differential.Fixture);
    }

    [Fact]
    public void RunWritesGeneratedArtifactBeforeReportingFailure()
    {
        var failure = new InvalidOperationException("compiler failed");
        var writer = new RecordingFailureWriter();
        var subject = new GeneratedCorpusRunner(
            new ThrowingDifferentialRunner(failure),
            writer,
            new SourceFailureReducer(TimeProvider.System));
        var generatedCase = Case();

        var actual = Assert.Throws<InvalidOperationException>(() => subject.Run(generatedCase));

        Assert.Same(generatedCase, writer.GeneratedCase);
        Assert.Same(failure, writer.Exception);
        Assert.Contains("/artifact", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureFingerprintTreatsLfAndCrLfAsLineBoundaries()
    {
        var reducer = new ProbingSourceReducer();
        var subject = new GeneratedCorpusRunner(
            new SequencedThrowingDifferentialRunner(
                new InvalidOperationException("same\r\ninitial detail"),
                new InvalidOperationException("same\ncandidate detail")),
            new RecordingFailureWriter(),
            reducer);

        Assert.Throws<InvalidOperationException>(() => subject.Run(Case()));

        Assert.True(reducer.CandidatePreservedFailure);
    }

    private static GeneratedCorpusCase Case() => new(
        "GeneratedCase",
        "Unit",
        42,
        ImmutableDictionary<string, string>.Empty.Add("dimension", "level"),
        CorrectnessTestAssets.CreateFixture("GeneratedCase"));

    private sealed class RecordingDifferentialRunner : IDifferentialCorpusRunner
    {
        public CorpusFixture? Fixture { get; private set; }

        public void Run(CorpusFixture fixture) => Fixture = fixture;
    }

    private sealed class ThrowingDifferentialRunner(Exception exception) :
        IDifferentialCorpusRunner
    {
        public void Run(CorpusFixture fixture) => throw exception;
    }

    private sealed class SequencedThrowingDifferentialRunner(
        Exception initial,
        Exception candidate) : IDifferentialCorpusRunner
    {
        private int _calls;

        public void Run(CorpusFixture fixture)
        {
            var exception = Interlocked.Increment(ref _calls) == 1
                ? initial
                : candidate;
            throw exception;
        }
    }

    private sealed class ProbingSourceReducer : ISourceFailureReducer
    {
        public bool CandidatePreservedFailure { get; private set; }

        public SourceReductionResult Reduce(SourceReductionRequest request)
        {
            CandidatePreservedFailure = request.PreservesFailure("candidate");
            return new(request.Source, request.Source, 1, true);
        }
    }

    private sealed class UnexpectedFailureWriter : IGeneratedFailureArtifactWriter
    {
        public string Write(
            GeneratedCorpusCase generatedCase,
            Exception exception,
            SourceReductionResult? reduction = null) =>
            throw new InvalidOperationException("failure writer should not run");
    }

    private sealed class RecordingFailureWriter : IGeneratedFailureArtifactWriter
    {
        public GeneratedCorpusCase? GeneratedCase { get; private set; }

        public Exception? Exception { get; private set; }

        public string Write(
            GeneratedCorpusCase generatedCase,
            Exception exception,
            SourceReductionResult? reduction = null)
        {
            GeneratedCase = generatedCase;
            Exception = exception;
            return "/artifact";
        }
    }
}
