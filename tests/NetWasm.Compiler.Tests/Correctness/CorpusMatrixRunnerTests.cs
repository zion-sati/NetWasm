using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusMatrixRunnerTests
{
    [Theory]
    [InlineData(0, "Release")]
    [InlineData(1, "Debug,Release")]
    [InlineData(2, "Debug,Release")]
    public void CSharpCompilesOncePerSelectedProfile(int profile, string expected)
    {
        var compiler = new RecordingCompiler();
        var directories = new RecordingDirectories();
        var execution = new RecordingCompiledRunner();
        var runner = Assert.IsAssignableFrom<IDifferentialCorpusRunner>(
            new DifferentialCorpusRunner(directories, compiler, execution, new CorpusMatrixExpander(), new RecordingCorpusCleanup()));

        runner.Run(Fixture() with { Matrix = new((CorpusMatrixProfile)profile) });

        Assert.Equal(1, directories.Calls);
        Assert.Equal(expected.Split(','), compiler.Compilations.Select(item => item.Profile.ToString()));
        Assert.Equal(compiler.Compilations, execution.Compilations);
    }

    [Theory]
    [InlineData("Release-Wasm64-Optimized")]
    [InlineData("Debug-Wasm32-Direct")]
    public void CSharpSingleCellDoesNotCompileOtherProfiles(string cell)
    {
        var compiler = new RecordingCompiler();
        var execution = new RecordingCompiledRunner();
        var runner = Assert.IsAssignableFrom<IDifferentialCorpusRunner>(
            new DifferentialCorpusRunner(new RecordingDirectories(), compiler, execution, new CorpusMatrixExpander(), new RecordingCorpusCleanup()));

        runner.Run(Fixture() with { Matrix = new(CorpusMatrixProfile.Extended, cell) });

        Assert.Equal(cell.Split('-')[0], Assert.Single(compiler.Compilations).Profile.ToString());
        Assert.Equal(compiler.Compilations, execution.Compilations);
    }

    [Fact]
    public void InvalidMatrixFailsBeforeAllocationOrCompilation()
    {
        var directories = new RecordingDirectories();
        var compiler = new RecordingCompiler();
        var execution = new RecordingCompiledRunner();
        var runner = Assert.IsAssignableFrom<IDifferentialCorpusRunner>(
            new DifferentialCorpusRunner(directories, compiler, execution, new CorpusMatrixExpander(), new RecordingCorpusCleanup()));

        Assert.Throws<ArgumentException>(() => runner.Run(Fixture() with
        {
            Matrix = new(CorpusMatrixProfile.Fast, "Release-Wasm64-Direct"),
        }));

        Assert.Equal(0, directories.Calls);
        Assert.Empty(compiler.Compilations);
        Assert.Empty(execution.Compilations);
    }

    [Theory]
    [InlineData("Release-Wasm32-Direct", false)]
    [InlineData("Release-Wasm64-Direct", false)]
    [InlineData("Release-Wasm32-Optimized", true)]
    [InlineData("Release-Wasm64-Optimized", true)]
    [InlineData("Emitted-Wasm64-Optimized", true)]
    public void CompiledCellUsesOnlyTheSelectedWidthAndTransformation(string cell, bool optimized)
    {
        var desktop = new RecordingDesktop();
        var netWasm = new RecordingNetWasm();
        var failures = new RecordingFailures();
        var runner = CreateRunner(desktop, netWasm, failures);
        var fixture = Fixture() with { Matrix = new(CorpusMatrixProfile.Extended, cell) };
        var profile = cell.StartsWith("Emitted", StringComparison.Ordinal) ? CilProfile.Emitted : CilProfile.Release;

        runner.Run(Compilation(fixture, profile));

        Assert.Equal(1, desktop.Calls);
        var call = Assert.Single(netWasm.Calls);
        Assert.Equal(cell.Contains("Wasm64", StringComparison.Ordinal) ? WasmTarget.Wasm64 : WasmTarget.Wasm32, call.Target);
        Assert.Equal(optimized, call.Fixture.ExecuteOptimizedWasm);
        Assert.True(call.Fixture.ExecuteWasm64);
        Assert.Empty(failures.Results);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptimizedOnlyComparesAgainstDesktopRatherThanWrongDirectOutput(bool optimizedAlsoWrong)
    {
        var netWasm = new RecordingNetWasm(directValue: -1, optimizedValue: optimizedAlsoWrong ? -1 : 42);
        var failures = new RecordingFailures();
        var runner = CreateRunner(new RecordingDesktop(), netWasm, failures);
        var fixture = Fixture() with { Matrix = new(CorpusMatrixProfile.Extended, "Release-Wasm64-Optimized") };

        if (optimizedAlsoWrong)
        {
            Assert.Throws<CorpusOracleMismatchException>(() => runner.Run(Compilation(fixture)));
            var result = Assert.Single(failures.Results);
            Assert.Equal(42, result.Desktop.Value);
            Assert.Equal(-1, result.NetWasm.Value);
            Assert.Equal("optimized.wasm", result.Execution.ModulePath);
            Assert.Equal("optimized-sha", result.Execution.ModuleSha256);
        }
        else
        {
            runner.Run(Compilation(fixture));
            Assert.Empty(failures.Results);
        }
        Assert.Single(netWasm.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrongCompiledProfileFailsBeforeAnyOracle(bool reused)
    {
        var desktop = new RecordingDesktop();
        var netWasm = new RecordingNetWasm();
        var runner = CreateRunner(desktop, netWasm, new RecordingFailures());
        var compilation = Compilation(Fixture() with { Matrix = new(CorpusMatrixProfile.Fast) }, CilProfile.Debug);

        Assert.Throws<ArgumentException>(() =>
        {
            if (reused)
            {
                var comparison = Assert.IsAssignableFrom<ICompiledCorpusComparisonRunner>(new CompiledCorpusComparisonRunner(
                    netWasm, CompiledCorpusTestFactory.RejectingLinked,
                    new CompiledCorpusTestFactory.RecordingReceiptDestinations(),
                    new CompiledCorpusTestFactory.RecordingLinkedReceipts(),
                    new OracleComparer(), new RecordingFailures(), new RandomCilCaseProgressReporter(TextWriter.Null),
                    new CorpusExpectationVerifier(), new CorpusExecutionVerifier(), new CorpusCompilationCellsSelector(new CorpusMatrixExpander())));
                comparison.RunAgainstOracle(compilation, Observations(42));
            }
            else
            {
                runner.Run(compilation);
            }
        });

        Assert.Equal(0, desktop.Calls);
        Assert.Empty(netWasm.Calls);
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    public void NamedMatrixRunsEachSelectedTargetOnce(int profile, int targets, bool optimized)
    {
        var netWasm = new RecordingNetWasm();
        var linked = new RecordingLinked();
        var failures = new RecordingFailures();
        var runner = CreateRunner(new RecordingDesktop(), netWasm, failures, linked);

        runner.Run(Compilation(Fixture() with { Matrix = new((CorpusMatrixProfile)profile) }));

        Assert.Equal(targets, netWasm.Calls.Count);
        Assert.All(netWasm.Calls, call => Assert.Equal(optimized, call.Fixture.ExecuteOptimizedWasm));
        Assert.Equal(profile == 2 ? 4 : 0, linked.Cells.Count);
        Assert.Empty(failures.Results);
    }

    private static CorpusFixture Fixture() => new("MatrixUnit", "MatrixUnit", "source", [7])
    {
        ExpectedReturnValue = 42,
    };

    private static CorpusCompilation Compilation(CorpusFixture fixture, CilProfile profile = CilProfile.Release)
    {
        var artifact = new CorpusArtifact("fixture.dll", "", "sha", "", "sdk", []);
        return new(fixture, profile, artifact, artifact, "in-memory");
    }

    private static ImmutableDictionary<int, OracleObservation> Observations(int value) =>
        ImmutableDictionary<int, OracleObservation>.Empty.Add(7, new(OracleObservationKind.Value, value, null, 0));

    private static ICompiledCorpusRunner CreateRunner(
        RecordingDesktop desktop,
        RecordingNetWasm netWasm,
        RecordingFailures failures,
        ILinkedCorpusOracleRunner? linked = null) =>
        CompiledCorpusTestFactory.Create(desktop, netWasm, new OracleComparer(), failures, new RandomCilCaseProgressReporter(TextWriter.Null),
            new FrozenOracleEvidenceReader(), new CorpusExpectationVerifier(), new CorpusExecutionVerifier(), new CorpusMatrixExpander(), CorrectnessTestAssets.HostIdentity, new CorpusSourceFingerprint(), linked);

    private sealed class RecordingLinked : ILinkedCorpusOracleRunner
    {
        public List<CorpusMatrixCell> Cells { get; } = [];

        public LinkedCorpusExecution CompileBuildAndRun(
            CorpusCompilation compilation,
            CorpusMatrixCell cell,
            CancellationToken cancellationToken = default)
        {
            Cells.Add(cell);
            return new(Observations(42), cell.Target, cell.Form,
                "application.wasm", "application-sha", "layout.json", "layout-sha",
                "manifest.json", "manifest-sha", "linked.wasm", "linked-sha", []);
        }
    }

    private sealed class RecordingDirectories : ICorpusRunDirectoryFactory
    {
        public int Calls { get; private set; }
        public string Create() { Calls++; return "in-memory"; }
    }

    private sealed class RecordingCompiler : IRoslynCorpusCompiler
    {
        public List<CorpusCompilation> Compilations { get; } = [];
        public CorpusCompilation Compile(CorpusFixture fixture, CilProfile profile, string outputDirectory)
        {
            var compilation = Compilation(fixture, profile) with { Directory = outputDirectory };
            Compilations.Add(compilation);
            return compilation;
        }
    }

    private sealed class RecordingCompiledRunner : ICompiledCorpusRunner
    {
        public List<CorpusCompilation> Compilations { get; } = [];
        public void Run(CorpusCompilation compilation, CancellationToken cancellationToken = default) => Compilations.Add(compilation);
    }

    private sealed class RecordingDesktop : IDesktopOracleRunner
    {
        public int Calls { get; private set; }
        public ImmutableDictionary<int, OracleObservation> Run(CorpusCompilation compilation, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Observations(42);
        }
    }

    private sealed class RecordingNetWasm(int directValue = 42, int optimizedValue = 42) : INetWasmOracleRunner
    {
        public List<(CorpusFixture Fixture, WasmTarget Target)> Calls { get; } = [];
        public NetWasmExecution CompileAndRun(CorpusCompilation compilation, WasmTarget target, CancellationToken cancellationToken = default)
        {
            Calls.Add((compilation.Fixture, target));
            var result = new NetWasmExecution(Observations(directValue), null, "direct.wasm", "direct-sha", target, true);
            return compilation.Fixture.ExecuteOptimizedWasm ? result with
            {
                OptimizedObservations = Observations(optimizedValue),
                OptimizedModulePath = "optimized.wasm",
                OptimizedModuleSha256 = "optimized-sha",
            } : result;
        }
    }

    private sealed class RecordingFailures : ICompilerFailureArtifactWriter
    {
        public List<(OracleObservation Desktop, OracleObservation NetWasm, NetWasmExecution Execution)> Results { get; } = [];
        public string Write(CorpusCompilation compilation, int input, OracleObservation desktop, OracleObservation netWasm,
            NetWasmExecution execution, string reason)
        {
            Results.Add((desktop, netWasm, execution));
            return "failure-bundle";
        }
    }
}
