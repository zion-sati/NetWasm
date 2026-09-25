using System.Security.Cryptography;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RoslynCorpusCompilerBoundaryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MaterializesAllUnitsAndPassesTheirPathsWithoutConcatenation(bool sameIl, bool optimized)
    {
        using var assets = new BoundaryAssets();
        var processes = new CompilerProcess();
        var compiler = assets.Compiler(processes);
        var fixture = CorrectnessTestAssets.CreateFixture("CompilerBoundary") with
        {
            AllowUnsafe = optimized,
            OracleMode = sameIl ? OracleMode.SameIl : OracleMode.SameSource,
            SameSourceReason = sameIl ? null : "different reference surfaces",
            AdditionalSources = [new("Nested/Support.cs.txt", "secondary\r\n")],
            NetWasmReferencePaths = ["additional-reference.dll"],
        };

        var compilation = compiler.Compile(fixture, optimized ? CilProfile.Release : CilProfile.Debug, assets.Output);

        Assert.Equal(sameIl ? 1 : 2, processes.Requests.Count);
        Assert.Equal(sameIl, ReferenceEquals(compilation.Desktop, compilation.NetWasm));
        Assert.Equal(["CompilerBoundary.cs", "Nested/Support.cs.txt"], compilation.Sources.Select(source => source.Name));
        Assert.Equal([fixture.Source, "secondary\r\n"], compilation.Sources.Select(source => File.ReadAllText(source.Path)));
        Assert.All(compilation.Sources, source => Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source.Path))), source.Sha256));
        Assert.All(processes.Requests, request =>
        {
            Assert.Equal(compilation.Sources.Select(source => source.Path), request.Arguments.TakeLast(2));
            Assert.Equal(optimized, request.Arguments.Contains("-unsafe+"));
            Assert.Contains(optimized ? "-optimize+" : "-optimize-", request.Arguments);
        });
        Assert.Contains("-reference:" + Path.Combine(assets.References, "reference.dll"), processes.Requests[0].Arguments);
        if (!sameIl)
        {
            Assert.Contains("-reference:additional-reference.dll", processes.Requests[1].Arguments);
            Assert.Contains("-reference:corelib.dll", processes.Requests[1].Arguments);
        }
        Assert.All(new[] { compilation.Desktop, compilation.NetWasm }, artifact =>
        {
            Assert.Equal("sdk-version", artifact.CompilerVersion);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[] { 42 })), artifact.AssemblySha256);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[] { 43 })), artifact.PdbSha256);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InvalidSourcesFailBeforeFilesOrProcesses(int invalid)
    {
        using var assets = new BoundaryAssets();
        var processes = new CompilerProcess();
        var fixture = CorrectnessTestAssets.CreateFixture("CompilerBoundary");
        fixture = invalid switch
        {
            0 => fixture with { AdditionalSources = default },
            1 => fixture with { Source = null! },
            2 => fixture with { AdditionalSources = [null!] },
            3 => fixture with { AdditionalSources = [new("Part.cs", null!)] },
            _ => fixture with { AdditionalSources = [new("compilerboundary.cs", "duplicate")] },
        };

        Assert.ThrowsAny<ArgumentException>(() => assets.Compiler(processes).Compile(fixture, CilProfile.Debug, assets.Output));

        Assert.False(Directory.Exists(assets.Output));
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public void InvalidOraclePolicyFailsBeforeSourceMaterialization()
    {
        using var assets = new BoundaryAssets();
        var processes = new CompilerProcess();
        var fixture = CorrectnessTestAssets.CreateFixture() with { SameSourceReason = null };

        Assert.Throws<InvalidOperationException>(() => assets.Compiler(processes).Compile(fixture, CilProfile.Debug, assets.Output));

        Assert.False(Directory.Exists(assets.Output));
        Assert.Empty(processes.Requests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FirstCompilerFailurePreservesCauseAndStopsLaterCalls(int failAt)
    {
        using var assets = new BoundaryAssets();
        var cause = new IOException("process failure");
        var processes = new CompilerProcess(failAt, cause);

        var error = Assert.Throws<InvalidOperationException>(() => assets.Compiler(processes).Compile(
            CorrectnessTestAssets.CreateFixture(), CilProfile.Debug, assets.Output));

        Assert.Same(cause, error.InnerException);
        Assert.Equal(failAt, processes.Requests.Count);
    }

    private sealed class BoundaryAssets : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("netwasm-source-boundary-");
        public string References => Path.Combine(_root.FullName, "references");
        public string Output => Path.Combine(_root.FullName, "output");

        public IRoslynCorpusCompiler Compiler(IQualifiedProcessRunner processes)
        {
            Directory.CreateDirectory(References);
            File.WriteAllBytes(Path.Combine(References, "reference.dll"), [1]);
            return Assert.IsAssignableFrom<IRoslynCorpusCompiler>(new RoslynCorpusCompiler(new("unused", "dotnet", "sdk-version", "csc.dll", "corelib.dll", References,
                "unused", "unused", "unused", TimeSpan.FromSeconds(1)), processes,
                CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier())));
        }

        public void Dispose() => _root.Delete(recursive: true);
    }

    private sealed class CompilerProcess(int failAt = 0, Exception? cause = null) : IQualifiedProcessRunner
    {
        public List<QualifiedProcessRequest> Requests { get; } = [];

        public QualifiedProcessResult Run(QualifiedProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Requests.Count == failAt)
            {
                return new(QualifiedProcessCompletion.Exited, 1, string.Empty, string.Empty, TimeSpan.Zero)
                {
                    LaunchException = cause,
                };
            }
            File.WriteAllBytes(request.Arguments.Single(argument => argument.StartsWith("-out:", StringComparison.Ordinal))[5..], [42]);
            File.WriteAllBytes(request.Arguments.Single(argument => argument.StartsWith("-pdb:", StringComparison.Ordinal))[5..], [43]);
            return new(QualifiedProcessCompletion.Exited, 0, string.Empty, string.Empty, TimeSpan.Zero);
        }
    }
}
