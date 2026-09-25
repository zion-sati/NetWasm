using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class FileCorpusRunnerTests
{
    [Fact]
    public void FileSourcePreservesAllDeclaredFixtureSettings()
    {
        var sources = new RecordingSources("exact source\r\n");
        var execution = new RecordingRunner();
        var subject = Assert.IsAssignableFrom<IFileCorpusRunner>(new FileCorpusRunner(sources, execution, new CorpusSourceNamesVerifier()));
        var fixture = Fixture() with
        {
            Inputs = [7, 9],
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(7, 100),
            ExpectedExceptionTypes = ImmutableDictionary<int, string>.Empty.Add(9, "System.OverflowException"),
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
        };

        subject.Run(fixture, "case/Program.cs.txt");

        Assert.Equal(["case/Program.cs.txt"], sources.Requests);
        var actual = Assert.Single(execution.Fixtures);
        Assert.Empty(actual.AdditionalSources);
        Assert.Equal(fixture with { Source = "exact source\r\n", AdditionalSources = actual.AdditionalSources }, actual);
        Assert.Equal(string.Empty, fixture.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void InvalidAssetIdentityFailsBeforeSourceRead(string? identity)
    {
        var sources = new RecordingSources("source");
        var execution = new RecordingRunner();
        var subject = Assert.IsAssignableFrom<IFileCorpusRunner>(new FileCorpusRunner(sources, execution, new CorpusSourceNamesVerifier()));

        Assert.ThrowsAny<ArgumentException>(() => subject.Run(Fixture(), identity!));

        Assert.Empty(sources.Requests);
        Assert.Empty(execution.Fixtures);
    }

    [Fact]
    public void NullFixtureFailsBeforeSourceRead()
    {
        var sources = new RecordingSources("source");
        var execution = new RecordingRunner();
        var subject = Assert.IsAssignableFrom<IFileCorpusRunner>(new FileCorpusRunner(sources, execution, new CorpusSourceNamesVerifier()));

        Assert.Throws<ArgumentNullException>(() => subject.Run(null!, "case/Program.cs.txt"));

        Assert.Empty(sources.Requests);
        Assert.Empty(execution.Fixtures);
    }

    [Theory]
    [InlineData("inline source")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ConflictingInlineSourceFailsBeforeSourceRead(string? inlineSource)
    {
        var sources = new RecordingSources("file source");
        var execution = new RecordingRunner();
        var subject = Assert.IsAssignableFrom<IFileCorpusRunner>(new FileCorpusRunner(sources, execution, new CorpusSourceNamesVerifier()));

        var error = Assert.Throws<ArgumentException>(() =>
            subject.Run(Fixture() with { Source = inlineSource! }, "case/Program.cs.txt"));

        Assert.Equal("fixture", error.ParamName);
        Assert.Empty(sources.Requests);
        Assert.Empty(execution.Fixtures);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void EmptyAssetCannotReachCompilation(string source)
    {
        var sources = new RecordingSources(source);
        var execution = new RecordingRunner();
        var subject = Assert.IsAssignableFrom<IFileCorpusRunner>(new FileCorpusRunner(sources, execution, new CorpusSourceNamesVerifier()));

        var error = Assert.Throws<InvalidOperationException>(() => subject.Run(Fixture(), "case/Program.cs.txt"));

        Assert.Equal("Corpus source asset is empty.", error.Message);
        Assert.Single(sources.Requests);
        Assert.Empty(execution.Fixtures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadAndExecutionFailuresPreserveTheOriginalCauseWithoutRetry(bool failExecution)
    {
        var cause = new InvalidOperationException("original failure");
        var sources = new RecordingSources("source", failExecution ? null : cause);
        var execution = new RecordingRunner(failExecution ? cause : null);
        var subject = Assert.IsAssignableFrom<IFileCorpusRunner>(new FileCorpusRunner(sources, execution, new CorpusSourceNamesVerifier()));

        var error = Assert.Throws<InvalidOperationException>(() => subject.Run(Fixture(), "case/Program.cs.txt"));

        Assert.Same(cause, error);
        Assert.Single(sources.Requests);
        Assert.Equal(failExecution ? 1 : 0, execution.Fixtures.Count);
    }

    private static CorpusFixture Fixture() => CorrectnessTestAssets.CreateFixture("FileCase") with
    {
        Source = string.Empty,
    };

    [Fact]
    public void MultipleAssetsRemainSeparateOrderedCompilationUnits()
    {
        var sources = new RecordingSources("exact text\r\n");
        var execution = new RecordingRunner();
        var subject = Assert.IsAssignableFrom<IFileCorpusRunner>(new FileCorpusRunner(sources, execution, new CorpusSourceNamesVerifier()));
        var fixture = Fixture();

        subject.Run(fixture, ["case/Entry.cs.txt", "case/Support.cs", "case/Nested/More.cs"]);

        Assert.Equal(["case/Entry.cs.txt", "case/Support.cs", "case/Nested/More.cs"], sources.Requests);
        var actual = Assert.Single(execution.Fixtures);
        Assert.Equal("exact text\r\n", actual.Source);
        Assert.Equal<CorpusSourceFile>([new("case/Support.cs", "exact text\r\n"), new("case/Nested/More.cs", "exact text\r\n")], actual.AdditionalSources);
        Assert.Equal(fixture with { Source = actual.Source, AdditionalSources = actual.AdditionalSources }, actual);
        Assert.Empty(fixture.AdditionalSources);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidOrConflictingFileSetsFailBeforeAnyRead(int invalid)
    {
        var sources = new RecordingSources("source");
        var execution = new RecordingRunner();
        var subject = Assert.IsAssignableFrom<IFileCorpusRunner>(new FileCorpusRunner(sources, execution, new CorpusSourceNamesVerifier()));
        var fixture = Fixture() with
        {
            AdditionalSources = invalid == 2 ? default : invalid == 3 ? [new("Existing.cs", "existing")] : [],
        };
        ImmutableArray<string> files = invalid == 0 ? [] : invalid == 1 ? ["File.cs", "file.cs"] : ["File.cs"];

        Assert.Throws<ArgumentException>(() => subject.Run(fixture, files));

        Assert.Empty(sources.Requests);
        Assert.Empty(execution.Fixtures);
    }

    private sealed class RecordingSources(string source, Exception? failure = null) : ICorpusSourceReader
    {
        public List<string> Requests { get; } = [];

        public string Read(string sourceFile)
        {
            Requests.Add(sourceFile);
            if (failure is not null)
            {
                throw failure;
            }
            return source;
        }
    }

    private sealed class RecordingRunner(Exception? failure = null) : IDifferentialCorpusRunner
    {
        public List<CorpusFixture> Fixtures { get; } = [];

        public void Run(CorpusFixture fixture)
        {
            Fixtures.Add(fixture);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
