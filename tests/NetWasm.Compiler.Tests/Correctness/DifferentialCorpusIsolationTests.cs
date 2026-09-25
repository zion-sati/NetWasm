namespace NetWasm.Compiler.Tests.Correctness;

public sealed class DifferentialCorpusIsolationTests
{
    [Fact]
    public void RepeatedRunsKeepBothProfilesUnderDistinctInvocationRoots()
    {
        var directories = new RecordingDirectories();
        var compiler = new RecordingCompiler();
        var execution = new RecordingExecution();
        var cleanup = new RecordingCorpusCleanup
        {
            BeforeClean = _ => Assert.Equal(directories.Calls * 2, execution.Compilations.Count),
        };
        var subject = Assert.IsAssignableFrom<IDifferentialCorpusRunner>(
            new DifferentialCorpusRunner(directories, compiler, execution, new CorpusMatrixExpander(), cleanup));
        var fixture = CorrectnessTestAssets.CreateFixture("RepeatedCase");

        subject.Run(fixture);
        subject.Run(fixture);

        Assert.Equal(2, directories.Calls);
        Assert.Equal(["run-1", "run-2"], cleanup.Directories);
        Assert.Equal(
            [Path.Combine("run-1", "Debug"), Path.Combine("run-1", "Release"),
             Path.Combine("run-2", "Debug"), Path.Combine("run-2", "Release")],
            compiler.Compilations.Select(compilation => compilation.Directory));
        Assert.Equal([CilProfile.Debug, CilProfile.Release, CilProfile.Debug, CilProfile.Release],
            compiler.Compilations.Select(compilation => compilation.Profile));
        Assert.All(compiler.Compilations, compilation => Assert.Same(fixture, compilation.Fixture));
        Assert.Equal(compiler.Compilations, execution.Compilations);
    }

    [Fact]
    public void NullFixtureFailsBeforeAllocatingOrCompiling()
    {
        var directories = new RecordingDirectories();
        var compiler = new RecordingCompiler();
        var execution = new RecordingExecution();
        var subject = Assert.IsAssignableFrom<IDifferentialCorpusRunner>(
            new DifferentialCorpusRunner(directories, compiler, execution, new CorpusMatrixExpander(), new RecordingCorpusCleanup()));

        Assert.Throws<ArgumentNullException>(() => subject.Run(null!));

        Assert.Equal(0, directories.Calls);
        Assert.Empty(compiler.Compilations);
        Assert.Empty(execution.Compilations);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 2, 2)]
    public void FirstFailureStopsLaterStepsAndPreservesOriginalCause(
        int failingStep, int compilationCalls, int executionCalls)
    {
        var cause = new InvalidOperationException("first failure");
        var directories = new RecordingDirectories(failingStep == 0 ? cause : null);
        var compiler = new RecordingCompiler(failingStep == 1 ? cause : null);
        var execution = new RecordingExecution(failingStep == 2 ? cause : null);
        var cleanup = new RecordingCorpusCleanup { Failure = failingStep == 3 ? cause : null };
        var subject = Assert.IsAssignableFrom<IDifferentialCorpusRunner>(
            new DifferentialCorpusRunner(directories, compiler, execution, new CorpusMatrixExpander(), cleanup));

        var actual = Assert.Throws<InvalidOperationException>(() =>
            subject.Run(CorrectnessTestAssets.CreateFixture("FailedCase")));

        Assert.Same(cause, actual);
        Assert.Equal(1, directories.Calls);
        Assert.Equal(compilationCalls, compiler.Compilations.Count);
        Assert.Equal(executionCalls, execution.Compilations.Count);
        Assert.Equal(failingStep == 3 ? ["run-1"] : Array.Empty<string>(), cleanup.Directories);
        Assert.Equal(failingStep == 0 ? null : "run-1", actual.Data["CorpusRunDirectory"]);
    }

    private sealed class RecordingDirectories(Exception? failure = null) : ICorpusRunDirectoryFactory
    {
        public int Calls { get; private set; }

        public string Create()
        {
            Calls++;
            if (failure is not null)
            {
                throw failure;
            }
            return $"run-{Calls}";
        }
    }

    private sealed class RecordingCompiler(Exception? failure = null) : IRoslynCorpusCompiler
    {
        public List<CorpusCompilation> Compilations { get; } = [];

        public CorpusCompilation Compile(CorpusFixture fixture, CilProfile profile, string outputDirectory)
        {
            var artifact = new CorpusArtifact("assembly", "pdb", "a", "p", "sdk", []);
            var compilation = new CorpusCompilation(fixture, profile, artifact, artifact, outputDirectory);
            Compilations.Add(compilation);
            if (failure is not null)
            {
                throw failure;
            }
            return compilation;
        }
    }

    private sealed class RecordingExecution(Exception? failure = null) : ICompiledCorpusRunner
    {
        public List<CorpusCompilation> Compilations { get; } = [];

        public void Run(CorpusCompilation compilation, CancellationToken cancellationToken = default)
        {
            Compilations.Add(compilation);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
