using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CompiledCorpusBoundaryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FrozenEvidenceMustMatchLiveDesktopBeforeNetWasm(bool missingFramework, bool exceptions)
    {
        var observation = exceptions ? new(OracleObservationKind.ManagedException, null, "Example.Exception", 0) : Value(42);
        var desktop = new Desktop(observation);
        var netWasm = new NetWasm(observation);
        var frozen = new Frozen(observation);
        var failures = new Failures();
        var identity = CorrectnessTestAssets.HostIdentity with { TargetFramework = missingFramework ? null : "tfm" };
        var runner = Runner(desktop, netWasm, frozen, failures, identity);
        var compilation = Compilation() with
        {
            Fixture = Compilation().Fixture with { FrozenOracleEvidencePath = "frozen.json" },
        };

        runner.Run(compilation);

        Assert.Equal(1, desktop.Calls);
        Assert.Equal(2, netWasm.Calls);
        var request = Assert.Single(frozen.Requests);
        Assert.Equal(new FrozenOracleRequest("frozen.json", compilation.Fixture.Name, compilation.Profile,
            compilation.Fixture.Inputs, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(compilation.Fixture.Source))),
            compilation.Desktop.AssemblySha256, compilation.Desktop.CompilerVersion,
            missingFramework ? string.Empty : "tfm", identity.RuntimeVersion, identity.FrameworkDescription, identity.ProcessArchitecture), request);
        Assert.Empty(failures.Results);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FrozenMismatchStopsBeforeNetWasm(bool liveException, bool frozenException)
    {
        var desktop = new Desktop(liveException ? new(OracleObservationKind.ManagedException, null, "Live.Exception", 0) : Value(42));
        var frozen = new Frozen(frozenException ? new(OracleObservationKind.ManagedException, null, "Frozen.Exception", 0) : Value(41));
        var netWasm = new NetWasm(Value(42));
        var failures = new Failures();
        var runner = Runner(desktop, netWasm, frozen, failures);
        var compilation = Compilation() with
        {
            Fixture = Compilation().Fixture with { FrozenOracleEvidencePath = "frozen.json" },
        };

        var error = Assert.Throws<InvalidOperationException>(() => runner.Run(compilation));

        Assert.Contains("frozen desktop oracle does not match", error.Message);
        Assert.Equal(1, desktop.Calls);
        Assert.Single(frozen.Requests);
        Assert.Equal(0, netWasm.Calls);
        Assert.Empty(failures.Results);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyOptionalOptimizationStillComparesAgainstDirect(bool artifactIdentity)
    {
        var failures = new Failures();
        var netWasm = new NetWasm(Value(42), Value(41), artifactIdentity);
        var runner = Runner(new Desktop(Value(42)), netWasm, new Frozen(Value(42)), failures);

        Assert.Throws<CorpusOracleMismatchException>(() => runner.Run(Compilation()));

        var result = Assert.Single(failures.Results);
        Assert.Equal(42, result.Reference.Value);
        Assert.Equal(41, result.Actual.Value);
        Assert.Equal(artifactIdentity ? "optimized.wasm" : "direct.wasm", result.Execution.ModulePath);
        Assert.Equal(artifactIdentity ? "optimized-sha" : "direct-sha", result.Execution.ModuleSha256);
        Assert.StartsWith("optimized Wasm changed raw Wasm: ", result.Reason);
    }

    [Fact]
    public void DirectOnlySelectionDoesNotAssertUnselectedOptimization()
    {
        var failures = new Failures();
        var netWasm = new NetWasm(Value(42), Value(-1));
        var runner = Runner(new Desktop(Value(42)), netWasm, new Frozen(Value(42)), failures);
        var compilation = Compilation() with
        {
            Fixture = Compilation().Fixture with { Matrix = new(CorpusMatrixProfile.Fast) },
        };

        runner.Run(compilation);

        Assert.Equal(1, netWasm.Calls);
        Assert.Empty(failures.Results);
    }

    [Fact]
    public void LegacyValidationOnlyTargetRemainsUnexecuted()
    {
        var failures = new Failures();
        var netWasm = new NetWasm(Value(42), validationOnly64: true);
        var runner = Runner(new Desktop(Value(42)), netWasm, new Frozen(Value(42)), failures);
        var compilation = Compilation() with
        {
            Fixture = Compilation().Fixture with { ExecuteWasm64 = false },
        };

        runner.Run(compilation);

        Assert.Equal(2, netWasm.Calls);
        Assert.Empty(failures.Results);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NullRequestsFailBeforeOracleWork(int request)
    {
        var desktop = new Desktop(Value(42));
        var netWasm = new NetWasm(Value(42));
        var runner = Runner(desktop, netWasm, new Frozen(Value(42)), new Failures());

        Assert.Throws<ArgumentNullException>(() =>
        {
            if (request == 0) runner.Run(null!);
            else Comparison(netWasm, new Failures()).RunAgainstOracle(request == 1 ? null! : Compilation(), request == 2 ? null! : Observations(Value(42)));
        });

        Assert.Equal(0, desktop.Calls);
        Assert.Equal(0, netWasm.Calls);
    }

    private static CorpusCompilation Compilation()
    {
        var artifact = new CorpusArtifact("fixture.dll", "", "assembly-sha", "", "sdk-version", []);
        return new(new("BoundaryUnit", "BoundaryUnit", "source", [0]), CilProfile.Release, artifact, artifact, "in-memory");
    }

    private static OracleObservation Value(int value) => new(OracleObservationKind.Value, value, null, 0);
    private static ImmutableDictionary<int, OracleObservation> Observations(OracleObservation observation) =>
        ImmutableDictionary<int, OracleObservation>.Empty.Add(0, observation);

    private static ICompiledCorpusRunner Runner(Desktop desktop, NetWasm netWasm, Frozen frozen, Failures failures,
        CorpusHostIdentity? identity = null) => CompiledCorpusTestFactory.Create(desktop, netWasm, new OracleComparer(), failures,
            new RandomCilCaseProgressReporter(TextWriter.Null), frozen, new CorpusExpectationVerifier(),
            new CorpusExecutionVerifier(), new CorpusMatrixExpander(), identity ?? CorrectnessTestAssets.HostIdentity, new CorpusSourceFingerprint());

    private static ICompiledCorpusComparisonRunner Comparison(NetWasm netWasm, Failures failures) =>
        Assert.IsAssignableFrom<ICompiledCorpusComparisonRunner>(new CompiledCorpusComparisonRunner(
            netWasm, CompiledCorpusTestFactory.RejectingLinked, new CompiledCorpusTestFactory.RecordingReceiptDestinations(),
            new CompiledCorpusTestFactory.RecordingLinkedReceipts(), new OracleComparer(), failures,
            new RandomCilCaseProgressReporter(TextWriter.Null), new CorpusExpectationVerifier(),
            new CorpusExecutionVerifier(), new CorpusCompilationCellsSelector(new CorpusMatrixExpander())));

    private sealed class Desktop(OracleObservation observation) : IDesktopOracleRunner
    {
        public int Calls { get; private set; }
        public ImmutableDictionary<int, OracleObservation> Run(CorpusCompilation compilation, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Observations(observation);
        }
    }

    private sealed class Frozen(OracleObservation observation) : IFrozenOracleEvidenceReader
    {
        public List<FrozenOracleRequest> Requests { get; } = [];
        public ImmutableDictionary<int, OracleObservation> Read(FrozenOracleRequest request)
        {
            Requests.Add(request);
            return Observations(observation);
        }
    }

    private sealed class NetWasm(OracleObservation direct, OracleObservation? optimized = null,
        bool artifactIdentity = true, bool validationOnly64 = false) : INetWasmOracleRunner
    {
        public int Calls { get; private set; }
        public NetWasmExecution CompileAndRun(CorpusCompilation compilation, WasmTarget target, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (validationOnly64 && target == WasmTarget.Wasm64)
            {
                return new(ImmutableDictionary<int, OracleObservation>.Empty, null, "direct.wasm", "direct-sha", target, false);
            }
            return new(Observations(direct), null, "direct.wasm", "direct-sha", target, true)
            {
                OptimizedObservations = optimized is null ? ImmutableDictionary<int, OracleObservation>.Empty : Observations(optimized),
                OptimizedModulePath = artifactIdentity ? "optimized.wasm" : null,
                OptimizedModuleSha256 = artifactIdentity ? "optimized-sha" : null,
            };
        }
    }

    private sealed class Failures : ICompilerFailureArtifactWriter
    {
        public List<(OracleObservation Reference, OracleObservation Actual, NetWasmExecution Execution, string Reason)> Results { get; } = [];
        public string Write(CorpusCompilation compilation, int input, OracleObservation desktop, OracleObservation netWasm,
            NetWasmExecution execution, string reason)
        {
            Results.Add((desktop, netWasm, execution, reason));
            return "failure-bundle";
        }
    }
}
