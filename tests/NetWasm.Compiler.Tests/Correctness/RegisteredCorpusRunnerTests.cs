using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RegisteredCorpusRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelegatesToTheExistingPipelineAndDoesNotActivateTheBuilder(bool emitted)
    {
        var boundary = new Boundaries();
        var manifest = CorpusCaseContractData.Manifest with { InputKind = emitted ? CorpusInputKind.Emitted : CorpusInputKind.CSharp };
        var runner = Runner(boundary);

        if (emitted)
        {
            runner.Run(manifest, "selected-cell", boundary, 1);
        }
        else
        {
            runner.Run(manifest, "selected-cell");
        }

        string[] expectedCalls = emitted ? ["fixture", "emitted"] : ["fixture", "files"];
        Assert.Equal(expectedCalls, boundary.Calls);
        Assert.Same(manifest, boundary.Manifest);
        Assert.Equal("selected-cell", boundary.Cell);
        Assert.Equal(emitted ? 1 : null, boundary.Input);
        Assert.Same(boundary.Created, boundary.Executed);
        if (emitted)
        {
            Assert.Same(boundary, boundary.Builder);
        }
        else
        {
            Assert.Equal(manifest.SourceFiles, boundary.Sources);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrongCategoryFailsBeforeMaterialization(bool emitted)
    {
        var boundary = new Boundaries();
        var manifest = CorpusCaseContractData.Manifest with { InputKind = emitted ? CorpusInputKind.CSharp : CorpusInputKind.Emitted };
        var runner = Runner(boundary);

        Assert.Throws<ArgumentException>(() =>
        {
            if (emitted) runner.Run(manifest, "cell", boundary);
            else runner.Run(manifest, "cell");
        });
        Assert.Empty(boundary.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullManifestFailsBeforeMaterialization(bool emitted)
    {
        var boundary = new Boundaries();
        var runner = Runner(boundary);
        Assert.Throws<ArgumentNullException>(() =>
        {
            if (emitted) runner.Run(null!, "cell", boundary);
            else runner.Run(null!, "cell");
        });
        Assert.Empty(boundary.Calls);
    }

    [Fact]
    public void MissingBuilderFailsBeforeMaterialization()
    {
        var boundary = new Boundaries();
        Assert.Throws<ArgumentNullException>(() => Runner(boundary).Run(
            CorpusCaseContractData.Manifest with { InputKind = CorpusInputKind.Emitted }, "cell", null!));
        Assert.Empty(boundary.Calls);
    }

    [Theory]
    [InlineData(false, "fixture", "fixture")]
    [InlineData(true, "fixture", "fixture")]
    [InlineData(false, "files", "fixture,files")]
    [InlineData(true, "emitted", "fixture,emitted")]
    public void RetainsFirstFailureWithoutRetry(bool emitted, string failing, string calls)
    {
        var boundary = new Boundaries { Failing = failing };
        var manifest = CorpusCaseContractData.Manifest with { InputKind = emitted ? CorpusInputKind.Emitted : CorpusInputKind.CSharp };
        var runner = Runner(boundary);

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            if (emitted) runner.Run(manifest, "cell", boundary);
            else runner.Run(manifest, "cell");
        });

        Assert.Same(boundary.Failure, failure);
        Assert.Equal(calls.Split(','), boundary.Calls);
    }

    private static IRegisteredCorpusRunner Runner(Boundaries boundaries) =>
        Assert.IsAssignableFrom<IRegisteredCorpusRunner>(new RegisteredCorpusRunner(boundaries, boundaries, boundaries));

    private sealed class Boundaries : ICorpusCaseFixtureFactory, IFileCorpusRunner, IEmittedCorpusRunner, IEmittedAssemblyBuilder
    {
        public List<string> Calls { get; } = [];
        public string? Failing { get; init; }
        public InvalidOperationException Failure { get; } = new("registered-runner-failure");
        public CorpusFixture Created { get; } = new("Created", "Created", "", [1]);
        public CorpusFixture? Executed { get; private set; }
        public CorpusCaseManifest? Manifest { get; private set; }
        public string? Cell { get; private set; }
        public int? Input { get; private set; }
        public IEmittedAssemblyBuilder? Builder { get; private set; }
        public ImmutableArray<string> Sources { get; private set; }

        public CorpusFixture Create(CorpusCaseManifest manifest, string cell, int? input = null)
        {
            Record("fixture");
            Manifest = manifest;
            Cell = cell;
            Input = input;
            return Created;
        }

        public void Run(CorpusFixture fixture, string sourceFile) => throw new NotSupportedException();

        public void Run(CorpusFixture fixture, ImmutableArray<string> sourceFiles)
        {
            Record("files");
            Executed = fixture;
            Sources = sourceFiles;
        }

        public void Run(CorpusFixture fixture, IEmittedAssemblyBuilder builder)
        {
            Record("emitted");
            Executed = fixture;
            Builder = builder;
        }

        public ImmutableArray<byte> Build() => throw new InvalidOperationException("Builder activation belongs to the emitted pipeline.");

        private void Record(string call)
        {
            Calls.Add(call);
            if (call == Failing)
            {
                throw Failure;
            }
        }
    }
}
