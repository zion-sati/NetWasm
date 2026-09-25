using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class EmittedCorpusRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunsOneIdenticalAssemblyWithAnHonestEmittedProfile(bool namedMatrix)
    {
        var builder = new RecordingBuilder();
        var writer = new RecordingWriter();
        var execution = new RecordingExecution();
        var cleanup = new RecordingCorpusCleanup { BeforeClean = _ => Assert.Single(execution.Compilations) };
        var subject = Assert.IsAssignableFrom<IEmittedCorpusRunner>(new EmittedCorpusRunner(writer, execution, new CorpusMatrixExpander(), cleanup));
        var fixture = Fixture() with { Matrix = namedMatrix ? new(CorpusMatrixProfile.Extended) : null };

        subject.Run(fixture, builder);

        Assert.Equal(1, builder.Calls);
        Assert.Equal(builder.Image, Assert.Single(writer.Images));
        var compilation = Assert.Single(execution.Compilations);
        Assert.Same(fixture, compilation.Fixture);
        Assert.Equal(CilProfile.Emitted, compilation.Profile);
        Assert.Same(writer.Result.Artifact, compilation.Desktop);
        Assert.Same(compilation.Desktop, compilation.NetWasm);
        Assert.Equal(writer.Result.Directory, compilation.Directory);
        Assert.Equal(writer.Result.Directory, Assert.Single(cleanup.Directories));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ConflictingInputDeclarationsFailBeforeBuilding(int invalidDeclaration)
    {
        var builder = new RecordingBuilder();
        var writer = new RecordingWriter();
        var execution = new RecordingExecution();
        var subject = Assert.IsAssignableFrom<IEmittedCorpusRunner>(new EmittedCorpusRunner(writer, execution, new CorpusMatrixExpander(), new RecordingCorpusCleanup()));
        var fixture = invalidDeclaration switch
        {
            0 => Fixture() with { Source = "inline source" },
            1 => Fixture() with { Source = null! },
            2 => Fixture() with { OracleMode = OracleMode.SameSource },
            3 => Fixture() with { OracleMode = OracleMode.FrozenDesktop },
            4 => Fixture() with { SameSourceReason = "conflict" },
            5 => Fixture() with { FrozenOracleEvidencePath = "conflict.json" },
            6 => Fixture() with { AdditionalSources = default },
            _ => Fixture() with { AdditionalSources = [new("Conflict.cs", "source")] },
        };

        var error = Assert.Throws<ArgumentException>(() => subject.Run(fixture, builder));

        Assert.Equal("fixture", error.ParamName);
        Assert.Equal(0, builder.Calls);
        Assert.Empty(writer.Images);
        Assert.Empty(execution.Compilations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullInputsFailBeforeBuilding(bool missingBuilder)
    {
        var builder = new RecordingBuilder();
        var writer = new RecordingWriter();
        var execution = new RecordingExecution();
        var subject = Assert.IsAssignableFrom<IEmittedCorpusRunner>(new EmittedCorpusRunner(writer, execution, new CorpusMatrixExpander(), new RecordingCorpusCleanup()));

        Assert.Throws<ArgumentNullException>(() =>
            subject.Run(missingBuilder ? Fixture() : null!, missingBuilder ? null! : builder));

        Assert.Equal(0, builder.Calls);
        Assert.Empty(writer.Images);
        Assert.Empty(execution.Compilations);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 1, 1)]
    public void FirstFailureStopsTheRemainingSteps(int failureStep, int writes, int executions)
    {
        var cause = new InvalidOperationException("first failure");
        var builder = new RecordingBuilder(failureStep == 0 ? cause : null);
        var writer = new RecordingWriter(failureStep == 1 ? cause : null);
        var execution = new RecordingExecution(failureStep == 2 ? cause : null);
        var cleanup = new RecordingCorpusCleanup { Failure = failureStep == 3 ? cause : null };
        var subject = Assert.IsAssignableFrom<IEmittedCorpusRunner>(new EmittedCorpusRunner(writer, execution, new CorpusMatrixExpander(), cleanup));

        var actual = Assert.Throws<InvalidOperationException>(() => subject.Run(Fixture(), builder));

        Assert.Same(cause, actual);
        Assert.Equal(1, builder.Calls);
        Assert.Equal(writes, writer.Images.Count);
        Assert.Equal(executions, execution.Compilations.Count);
        Assert.Equal(failureStep == 3 ? [writer.Result.Directory] : Array.Empty<string>(), cleanup.Directories);
        Assert.Equal(failureStep < 2 ? null : writer.Result.Directory, actual.Data["CorpusRunDirectory"]);
    }

    [Fact]
    public void RoslynProfilesDoNotIncludeEmittedAssemblies() =>
        Assert.Equal<CilProfile>([CilProfile.Debug, CilProfile.Release], CilProfiles.Roslyn);

    [Fact]
    public void RoslynReplayCellFailsBeforeEmittingOrWriting()
    {
        var builder = new RecordingBuilder();
        var writer = new RecordingWriter();
        var execution = new RecordingExecution();
        var subject = Assert.IsAssignableFrom<IEmittedCorpusRunner>(new EmittedCorpusRunner(writer, execution, new CorpusMatrixExpander(), new RecordingCorpusCleanup()));

        Assert.Throws<ArgumentException>(() => subject.Run(Fixture() with
        {
            Matrix = new(CorpusMatrixProfile.Extended, "Release-Wasm32-Direct"),
        }, builder));

        Assert.Equal(0, builder.Calls);
        Assert.Empty(writer.Images);
        Assert.Empty(execution.Compilations);
    }

    private static CorpusFixture Fixture() => new("EmittedUnit", "EmittedUnit", string.Empty, [0, 1])
    {
        OracleMode = OracleMode.SameIl,
        SameSourceReason = null,
        ExpectedReturnValue = 42,
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
    };

    private sealed class RecordingBuilder(Exception? failure = null) : IEmittedAssemblyBuilder
    {
        public ImmutableArray<byte> Image { get; } = [1, 2, 3];
        public int Calls { get; private set; }

        public ImmutableArray<byte> Build()
        {
            Calls++;
            if (failure is not null)
            {
                throw failure;
            }
            return Image;
        }
    }

    private sealed class RecordingWriter(Exception? failure = null) : ICorpusAssemblyWriter
    {
        public List<ImmutableArray<byte>> Images { get; } = [];
        public PersistedCorpusAssembly Result { get; } = new(
            new("fixture.dll", string.Empty, "assembly-hash", string.Empty, "not-applicable", []), "run");

        public PersistedCorpusAssembly Write(ImmutableArray<byte> image)
        {
            Images.Add(image);
            if (failure is not null)
            {
                throw failure;
            }
            return Result;
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
