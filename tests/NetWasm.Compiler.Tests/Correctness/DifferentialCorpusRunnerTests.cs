using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class DifferentialCorpusRunnerTests
{
    [Fact]
    public void RunCompilesBothProfilesAndExecutesBothTargets()
    {
        var fixture = CorrectnessTestAssets.CreateFixture("OrchestratorUnit");
        var compiler = new RecordingCompiler();
        var desktop = new ConstantDesktopRunner();
        var netWasm = new RecordingNetWasmRunner();
        var runner = new DifferentialCorpusRunner(
            new InMemoryDirectoryFactory(),
            compiler,
            CompiledCorpusTestFactory.Create(
                desktop,
                netWasm,
                new OracleComparer(),
                new UnexpectedFailureWriter(),
                new RandomCilCaseProgressReporter(TextWriter.Null),
                new FrozenOracleEvidenceReader(),
                new CorpusExpectationVerifier(),
                new CorpusExecutionVerifier(), new CorpusMatrixExpander(), CorrectnessTestAssets.HostIdentity, new CorpusSourceFingerprint()), new CorpusMatrixExpander(), new RecordingCorpusCleanup());

        runner.Run(fixture);

        Assert.Equal([CilProfile.Debug, CilProfile.Release], compiler.Profiles);
        Assert.Equal(
            [WasmTarget.Wasm32, WasmTarget.Wasm64, WasmTarget.Wasm32, WasmTarget.Wasm64],
            netWasm.Targets);
    }

    [Fact]
    public void RunWritesFailureArtifactBeforeReportingMismatch()
    {
        var failures = new RecordingFailureWriter();
        var runner = new DifferentialCorpusRunner(
            new InMemoryDirectoryFactory(),
            new RecordingCompiler(),
            CompiledCorpusTestFactory.Create(
                new ConstantDesktopRunner(),
                new RecordingNetWasmRunner(value: 99),
                new OracleComparer(),
                failures,
                new RandomCilCaseProgressReporter(TextWriter.Null),
                new FrozenOracleEvidenceReader(),
                new CorpusExpectationVerifier(),
                new CorpusExecutionVerifier(), new CorpusMatrixExpander(), CorrectnessTestAssets.HostIdentity, new CorpusSourceFingerprint()), new CorpusMatrixExpander(), new RecordingCorpusCleanup());

        var exception = Assert.Throws<CorpusOracleMismatchException>(() =>
            runner.Run(CorrectnessTestAssets.CreateFixture("MismatchUnit")));

        Assert.Equal(1, failures.Count);
        Assert.Contains("reproduction: artifact-directory", exception.Message);
    }

    [Fact]
    public void RunAgainstOracleDoesNotExecuteDesktopAgain()
    {
        var fixture = CorrectnessTestAssets.CreateFixture("SharedOracleUnit");
        var compilation = new RecordingCompiler().Compile(
            fixture,
            CilProfile.Debug,
            Path.GetTempPath());
        var expected = new ConstantDesktopRunner().Run(compilation);
        var netWasm = new RecordingNetWasmRunner();
        var runner = Assert.IsAssignableFrom<ICompiledCorpusComparisonRunner>(new CompiledCorpusComparisonRunner(
            netWasm,
            CompiledCorpusTestFactory.RejectingLinked,
            new CompiledCorpusTestFactory.RecordingReceiptDestinations(),
            new CompiledCorpusTestFactory.RecordingLinkedReceipts(),
            new OracleComparer(),
            new UnexpectedFailureWriter(),
            new RandomCilCaseProgressReporter(TextWriter.Null),
            new CorpusExpectationVerifier(),
            new CorpusExecutionVerifier(), new CorpusCompilationCellsSelector(new CorpusMatrixExpander())));

        runner.RunAgainstOracle(compilation, expected);

        Assert.Equal(
            [NetWasm.Compiler.Core.WasmTarget.Wasm32, NetWasm.Compiler.Core.WasmTarget.Wasm64],
            netWasm.Targets);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void FailedIndependentExpectationStopsBeforeNetWasmEvenWhenResultsWouldAgree(bool reuseOracle, int expectationKind)
    {
        var fixture = CorrectnessTestAssets.CreateFixture("FailedSuccessContract") with
        {
            Inputs = [3],
            ExpectedReturnValue = expectationKind == 0 ? 100 : null,
            ExpectedReturnValues = expectationKind == 1
                ? ImmutableDictionary<int, int>.Empty.Add(3, 100)
                : ImmutableDictionary<int, int>.Empty,
            ExpectedExceptionTypes = expectationKind == 2
                ? ImmutableDictionary<int, string>.Empty.Add(3, "System.InvalidOperationException")
                : ImmutableDictionary<int, string>.Empty,
        };
        var compilation = new RecordingCompiler().Compile(fixture, CilProfile.Debug, "unused");
        var desktop = new ConstantDesktopRunner();
        var netWasm = new RecordingNetWasmRunner(value: 3);
        var runner = CompiledCorpusTestFactory.Create(
            desktop,
            netWasm,
            new OracleComparer(),
            new UnexpectedFailureWriter(),
            new RandomCilCaseProgressReporter(TextWriter.Null),
            new FrozenOracleEvidenceReader(),
            new CorpusExpectationVerifier(),
            new CorpusExecutionVerifier(), new CorpusMatrixExpander(), CorrectnessTestAssets.HostIdentity, new CorpusSourceFingerprint());

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            if (reuseOracle)
            {
                var comparison = Assert.IsAssignableFrom<ICompiledCorpusComparisonRunner>(new CompiledCorpusComparisonRunner(
                    netWasm, CompiledCorpusTestFactory.RejectingLinked,
                    new CompiledCorpusTestFactory.RecordingReceiptDestinations(),
                    new CompiledCorpusTestFactory.RecordingLinkedReceipts(),
                    new OracleComparer(), new UnexpectedFailureWriter(),
                    new RandomCilCaseProgressReporter(TextWriter.Null), new CorpusExpectationVerifier(),
                    new CorpusExecutionVerifier(), new CorpusCompilationCellsSelector(new CorpusMatrixExpander())));
                comparison.RunAgainstOracle(compilation, desktop.Run(compilation));
            }
            else
            {
                runner.Run(compilation);
            }
        });

        Assert.Contains("independent expected", exception.Message);
        Assert.Empty(netWasm.Targets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiredExecutionCannotDisappearFromTheComparison(bool memory64)
    {
        var target = memory64 ? WasmTarget.Wasm64 : WasmTarget.Wasm32;
        var netWasm = new RecordingNetWasmRunner(skippedTarget: target);
        var runner = new DifferentialCorpusRunner(new InMemoryDirectoryFactory(), new RecordingCompiler(), CompiledCorpusTestFactory.Create(
            new ConstantDesktopRunner(), netWasm, new OracleComparer(), new UnexpectedFailureWriter(),
            new RandomCilCaseProgressReporter(TextWriter.Null), new FrozenOracleEvidenceReader(),
            new CorpusExpectationVerifier(), new CorpusExecutionVerifier(), new CorpusMatrixExpander(), CorrectnessTestAssets.HostIdentity, new CorpusSourceFingerprint()), new CorpusMatrixExpander(), new RecordingCorpusCleanup());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            runner.Run(CorrectnessTestAssets.CreateFixture("RequiredExecution")));

        Assert.Contains("execution was skipped", exception.Message);
        Assert.Equal(memory64 ? [WasmTarget.Wasm32, WasmTarget.Wasm64] : [WasmTarget.Wasm32], netWasm.Targets);
    }

    [Fact]
    public void MissingRequiredOptimizationStopsBeforeTheNextTarget()
    {
        var netWasm = new RecordingNetWasmRunner();
        var runner = new DifferentialCorpusRunner(new InMemoryDirectoryFactory(), new RecordingCompiler(), CompiledCorpusTestFactory.Create(
            new ConstantDesktopRunner(), netWasm, new OracleComparer(), new UnexpectedFailureWriter(),
            new RandomCilCaseProgressReporter(TextWriter.Null), new FrozenOracleEvidenceReader(),
            new CorpusExpectationVerifier(), new CorpusExecutionVerifier(), new CorpusMatrixExpander(), CorrectnessTestAssets.HostIdentity, new CorpusSourceFingerprint()), new CorpusMatrixExpander(), new RecordingCorpusCleanup());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            runner.Run(CorrectnessTestAssets.CreateFixture("RequiredOptimization") with
            {
                ExecuteOptimizedWasm = true,
            }));

        Assert.Contains("optimized artifact identity is missing", exception.Message);
        Assert.Equal([WasmTarget.Wasm32], netWasm.Targets);
    }

    private sealed class InMemoryDirectoryFactory : ICorpusRunDirectoryFactory
    {
        public string Create() => "in-memory-run";
    }

    private sealed class RecordingCompiler : IRoslynCorpusCompiler
    {
        public List<CilProfile> Profiles { get; } = [];

        public CorpusCompilation Compile(
            CorpusFixture fixture,
            CilProfile profile,
            string outputDirectory)
        {
            Profiles.Add(profile);
            var artifact = new CorpusArtifact("assembly", "pdb", "a", "p", "sdk", []);
            return new(fixture, profile, artifact, artifact, outputDirectory);
        }
    }

    private sealed class ConstantDesktopRunner : IDesktopOracleRunner
    {
        public ImmutableDictionary<int, OracleObservation> Run(
            CorpusCompilation compilation,
            CancellationToken cancellationToken = default) =>
            compilation.Fixture.Inputs.ToImmutableDictionary(
            input => input,
            input => new OracleObservation(OracleObservationKind.Value, input, null, input));
    }

    private sealed class RecordingNetWasmRunner(int? value = null, WasmTarget? skippedTarget = null) : INetWasmOracleRunner
    {
        public List<WasmTarget> Targets { get; } = [];

        public NetWasmExecution CompileAndRun(
            CorpusCompilation compilation,
            WasmTarget target,
            CancellationToken cancellationToken = default)
        {
            Targets.Add(target);
            var observations = compilation.Fixture.Inputs.ToImmutableDictionary(
                    input => input,
                    input => new OracleObservation(
                        OracleObservationKind.Value,
                        value ?? input,
                        null,
                        input));
            return new(observations, "trace", "module", "sha", target,
                Executed: target != skippedTarget);
        }
    }

    private sealed class UnexpectedFailureWriter : ICompilerFailureArtifactWriter
    {
        public string Write(
            CorpusCompilation compilation,
            int input,
            OracleObservation desktop,
            OracleObservation netWasm,
            NetWasmExecution execution,
            string reason) => throw new InvalidOperationException("unexpected failure");
    }

    private sealed class RecordingFailureWriter : ICompilerFailureArtifactWriter
    {
        public int Count { get; private set; }

        public string Write(
            CorpusCompilation compilation,
            int input,
            OracleObservation desktop,
            OracleObservation netWasm,
            NetWasmExecution execution,
            string reason)
        {
            Count++;
            return "artifact-directory";
        }
    }
}
