using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusRunLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SuccessfulRunsReleaseOnlyTheirRootAndFailedRunsRetainInputs(bool emitted, bool fails)
    {
        var directories = new Directories(new CorpusRunDirectoryFactory());
        var cause = new InvalidOperationException("execution failure");
        var execution = new Execution(fails ? cause : null);
        var fixture = new CorpusFixture("Lifetime", "Lifetime", string.Empty, [0])
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
        };
        try
        {
            void Run()
            {
                if (emitted)
                {
                    var runner = Assert.IsAssignableFrom<IEmittedCorpusRunner>(new EmittedCorpusRunner(
                        new CorpusAssemblyWriter(directories), execution, new CorpusMatrixExpander(), new CorpusRunDirectoryCleaner()));
                    runner.Run(fixture, new AssemblyBuilder());
                }
                else
                {
                    var runner = Assert.IsAssignableFrom<IDifferentialCorpusRunner>(new DifferentialCorpusRunner(
                        directories, new Compiler(), execution, new CorpusMatrixExpander(), new CorpusRunDirectoryCleaner()));
                    runner.Run(fixture);
                }
            }

            if (fails) Assert.Same(cause, Assert.Throws<InvalidOperationException>(Run));
            else Run();

            var root = Assert.Single(directories.Roots);
            Assert.Equal(fails, Directory.Exists(root));
            Assert.Equal(fails ? root : null, cause.Data["CorpusRunDirectory"]);
            Assert.Equal(emitted || fails ? 1 : 2, execution.Calls);
            if (fails)
            {
                var input = emitted ? Path.Combine(root, "fixture.dll") : Path.Combine(root, "Debug", "fixture.dll");
                Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(input));
            }
        }
        finally
        {
            foreach (var root in directories.Roots)
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class Directories(ICorpusRunDirectoryFactory factory) : ICorpusRunDirectoryFactory
    {
        public List<string> Roots { get; } = [];
        public string Create()
        {
            var root = factory.Create();
            Roots.Add(root);
            return root;
        }
    }

    private sealed class Compiler : IRoslynCorpusCompiler
    {
        public CorpusCompilation Compile(CorpusFixture fixture, CilProfile profile, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, "fixture.dll");
            File.WriteAllBytes(path, [1, 2, 3]);
            var artifact = new CorpusArtifact(path, "", "hash", "", "", []);
            return new(fixture, profile, artifact, artifact, outputDirectory);
        }
    }

    private sealed class Execution(Exception? failure) : ICompiledCorpusRunner
    {
        public int Calls { get; private set; }
        public void Run(CorpusCompilation compilation, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(compilation.Desktop.AssemblyPath));
            if (failure is not null) throw failure;
        }
    }

    private sealed class AssemblyBuilder : IEmittedAssemblyBuilder
    {
        public ImmutableArray<byte> Build() => [1, 2, 3];
    }
}
