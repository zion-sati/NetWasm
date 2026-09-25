namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class RoslynCorpusCompilerTests
{
    [Theory]
    [InlineData((int)CilProfile.Emitted)]
    [InlineData(-1)]
    [InlineData(99)]
    public void NonRoslynProfilesFailBeforeFilesOrProcessesAreCreated(int profile)
    {
        var root = Directory.CreateTempSubdirectory("netwasm-roslyn-profile-");
        try
        {
            var processes = new UnexpectedProcesses();
            var environment = new CompilerCorrectnessEnvironment(
                "unused", "unused", "sdk", "unused", "unused", "unused", "unused", "unused", "unused",
                TimeSpan.FromSeconds(1));
            var compiler = Assert.IsAssignableFrom<IRoslynCorpusCompiler>(new RoslynCorpusCompiler(
                environment, processes, CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier())));

            var error = Assert.Throws<ArgumentOutOfRangeException>(() => compiler.Compile(
                CorrectnessTestAssets.CreateFixture(), (CilProfile)profile, Path.Combine(root.FullName, "output")));

            Assert.Equal("profile", error.ParamName);
            Assert.Equal(0, processes.Calls);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void CompileUsesPinnedLatestRoslynAndRecordsPortableArtifacts()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var compiler = new RoslynCorpusCompiler(
            environment,
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()));
        foreach (var profile in CilProfiles.Roslyn)
        {
            var directory = CorrectnessTestAssets.CreateDirectory();
            var compilation = compiler.Compile(
                CorrectnessTestAssets.CreateFixture("RoslynCorpusCompilerUnit"),
                profile,
                directory);

            Assert.Equal(profile, compilation.Profile);
            Assert.All(
                [compilation.Desktop, compilation.NetWasm],
                artifact =>
                {
                    Assert.True(File.Exists(artifact.AssemblyPath));
                    Assert.True(File.Exists(artifact.PdbPath));
                    Assert.Equal(64, artifact.AssemblySha256.Length);
                    Assert.Equal(64, artifact.PdbSha256.Length);
                    Assert.Equal(environment.SdkVersion, artifact.CompilerVersion);
                    Assert.Contains("-langversion:latest", artifact.CompilerOptions);
                    Assert.Contains(
                        profile == CilProfile.Release ? "-optimize+" : "-optimize-",
                        artifact.CompilerOptions);
                });
        }
    }

    private sealed class UnexpectedProcesses : IQualifiedProcessRunner
    {
        public int Calls { get; private set; }

        public QualifiedProcessResult Run(QualifiedProcessRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("No process is permitted for a non-Roslyn profile.");
        }
    }
}
