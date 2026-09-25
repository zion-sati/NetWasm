using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class NetWasmOracleRunnerTests
{
    private static CorpusApplicationCompiler CreateApplicationCompiler(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        IOracleOperationProgressReporter? progress = null,
        ICorpusCompilerFailureWriter? failures = null)
    {
        failures ??= new RecordingCompilerFailures();
        return new(environment, new CorpusCompilerRequestWriter(), processes,
            progress ?? new OracleOperationProgressReporter(new OracleOperationProgressPlan()),
            new CorpusCompilerProcessVerifier(failures),
            new CorpusCompilerResponseReader(new CorpusCompilerResponseParser(), failures));
    }

    [Fact]
    public void CompileAndRunExecutesWasm32AndWasm64()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var compilation = services.GetRequiredService<IRoslynCorpusCompiler>().Compile(
            CorrectnessTestAssets.CreateFixture("NetWasmOracleRunnerUnit") with
            {
                ExecuteOptimizedWasm = true,
            },
            CilProfile.Debug,
            CorrectnessTestAssets.CreateDirectory());
        var runner = services.GetRequiredService<INetWasmOracleRunner>();

        var wasm32 = runner.CompileAndRun(compilation, WasmTarget.Wasm32);
        var wasm64 = runner.CompileAndRun(compilation, WasmTarget.Wasm64);

        Assert.True(wasm32.Executed);
        Assert.Equal(OracleObservationKind.Value, wasm32.Observations[3].Kind);
        Assert.Equal(4, wasm32.Observations[3].Value);
        Assert.Equal(
            "System.ArgumentOutOfRangeException",
            wasm32.Observations[-1].ExceptionType);
        Assert.True(wasm64.Executed);
        Assert.Equal(OracleObservationKind.Value, wasm64.Observations[3].Kind);
        Assert.Equal(4, wasm64.Observations[3].Value);
        Assert.Equal(
            "System.ArgumentOutOfRangeException",
            wasm64.Observations[-1].ExceptionType);
        Assert.True(File.Exists(wasm32.ModulePath));
        Assert.True(File.Exists(wasm64.ModulePath));
        Assert.True(File.Exists(Path.ChangeExtension(
            wasm32.ModulePath, ".optimized.wasm")));
        Assert.Equal(64, wasm32.ModuleSha256.Length);
        Assert.Equal(64, wasm64.ModuleSha256.Length);
        Assert.Null(wasm32.DiagnosticTracePath);
        Assert.Null(wasm64.DiagnosticTracePath);
    }

    [Fact]
    public void BatchedNodeProtocolMatchesIsolatedPerInputObservations()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var compilation = services.GetRequiredService<IRoslynCorpusCompiler>().Compile(
            CorrectnessTestAssets.CreateFixture("NetWasmOracleBatchUnit") with { Inputs = [3, -1, 7] },
            CilProfile.Debug,
            CorrectnessTestAssets.CreateDirectory());
        var progress = new RecordingOracleOperationProgressReporter();
        var runner = new NetWasmOracleRunner(
            services.GetRequiredService<CompilerCorrectnessEnvironment>(),
            new QualifiedProcessRunner(),
            services.GetRequiredService<ICorpusCompilerRequestFactory>(),
            progress,
            new CorpusExportFactory(),
            new OracleRuntimeCapabilityVerifier(),
            CreateApplicationCompiler(services.GetRequiredService<CompilerCorrectnessEnvironment>(), new QualifiedProcessRunner(), progress));

        var isolated = runner.CompileAndRun(compilation, WasmTarget.Wasm32);
        var batched = runner.CompileAndRun(compilation with
        {
            Fixture = compilation.Fixture with
            {
                SupportsBatchedOracle = true,
            },
        }, WasmTarget.Wasm32);

        Assert.Equal(isolated.Observations.Keys.Order(), batched.Observations.Keys.Order());
        foreach (var input in isolated.Observations.Keys)
        {
            var expected = isolated.Observations[input];
            var actual = batched.Observations[input];
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Value, actual.Value);
            Assert.Equal(expected.ExceptionType, actual.ExceptionType);
            Assert.Equal(expected.Trace, actual.Trace);
            Assert.Equal(expected.Detail, actual.Detail);
            Assert.Equal(
                expected.TraceRecords.AsEnumerable(),
                actual.TraceRecords.AsEnumerable());
        }
        Assert.Contains(progress.InputReports, report => report.Completed == 1);
        Assert.Contains(
            progress.CompilerReports,
            report => report is { Completed: 10, Total: 10 });

        var wasm64 = runner.CompileAndRun(compilation with
        {
            Fixture = compilation.Fixture with
            {
                ExecuteWasm64 = true,
                SupportsBatchedOracle = true,
            },
        }, WasmTarget.Wasm64);

        Assert.True(wasm64.Executed);
        Assert.Equal(4, wasm64.Observations[3].Value);
        Assert.Equal(OracleObservationKind.Value, wasm64.Observations[7].Kind);
        Assert.Equal(8, wasm64.Observations[7].Value);
        Assert.Equal(
            "System.ArgumentOutOfRangeException",
            wasm64.Observations[-1].ExceptionType);
    }

    [Fact]
    public void DiagnosticCaptureIsOptIn()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var fixture = CorrectnessTestAssets.CreateFixture("DiagnosticCaptureUnit") with
        {
            CaptureCompilerDiagnostics = true,
        };
        var compilation = services.GetRequiredService<IRoslynCorpusCompiler>().Compile(
            fixture,
            CilProfile.Debug,
            CorrectnessTestAssets.CreateDirectory());

        var execution = services.GetRequiredService<INetWasmOracleRunner>()
            .CompileAndRun(compilation, WasmTarget.Wasm32);

        Assert.NotNull(execution.DiagnosticTracePath);
        Assert.True(File.Exists(execution.DiagnosticTracePath));
        Assert.True(Directory.Exists(execution.DiagnosticTracePath + ".passes"));
    }

    [Theory]
    [InlineData(134)]
    [InlineData(138)]
    [InlineData(139)]
    [InlineData(-1073741819)]
    [InlineData(-1073740791)]
    [InlineData(1)]
    public void DoesNotRetryCompilerFailuresEvenIfNextAttemptWouldSucceed(int exitCode)
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var fixture = CorrectnessTestAssets.CreateFixture("NativeCompilerChild");
        var artifact = new CorpusArtifact(
            "input.dll",
            "input.pdb",
            "assembly-sha",
            "pdb-sha",
            environment.SdkVersion,
            []);
        var compilation = new CorpusCompilation(
            fixture,
            CilProfile.Debug,
            artifact,
            artifact,
            CorrectnessTestAssets.CreateDirectory());
        var processes = new ExitingCompilerProcessRunner(exitCode);
        var runner = new NetWasmOracleRunner(
            environment,
            processes,
            new CorpusCompilerRequestFactory(environment, CorrectnessTestAssets.CreateOracleModes()),
            new OracleOperationProgressReporter(new OracleOperationProgressPlan()),
            new CorpusExportFactory(),
            new OracleRuntimeCapabilityVerifier(),
            CreateApplicationCompiler(environment, processes));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            runner.CompileAndRun(compilation, WasmTarget.Wasm32));

        Assert.Contains($"exit={exitCode}", exception.Message);
        Assert.Equal(1, processes.Calls);
    }

    [Fact]
    public void InfiniteCompilerChildFailsAsABoundedTimeout()
    {
        var discovered = CompilerCorrectnessEnvironment.Discover();
        var environment = discovered with
        {
            ProcessTimeout = TimeSpan.FromMilliseconds(200),
        };
        var fixture = CorrectnessTestAssets.CreateFixture("InfiniteCompilerChild");
        var artifact = new CorpusArtifact(
            "input.dll",
            "input.pdb",
            "assembly-sha",
            "pdb-sha",
            environment.SdkVersion,
            []);
        var compilation = new CorpusCompilation(
            fixture,
            CilProfile.Debug,
            artifact,
            artifact,
            CorrectnessTestAssets.CreateDirectory());
        var runner = new NetWasmOracleRunner(
            environment,
            new InfiniteCompilerProcessRunner(),
            new CorpusCompilerRequestFactory(environment, CorrectnessTestAssets.CreateOracleModes()),
            new OracleOperationProgressReporter(new OracleOperationProgressPlan()),
            new CorpusExportFactory(),
            new OracleRuntimeCapabilityVerifier(),
            CreateApplicationCompiler(environment, new InfiniteCompilerProcessRunner()));

        var exception = Assert.Throws<TimeoutException>(() =>
            runner.CompileAndRun(compilation, WasmTarget.Wasm32));

        Assert.Contains("compiler exceeded", exception.Message);
    }

    [Theory]
    [InlineData(1, WasmTarget.Wasm32, false)]
    [InlineData(2, WasmTarget.Wasm32, false)]
    [InlineData(4, WasmTarget.Wasm32, false)]
    [InlineData(1, WasmTarget.Wasm64, false)]
    [InlineData(2, WasmTarget.Wasm64, false)]
    [InlineData(4, WasmTarget.Wasm64, false)]
    [InlineData(1, WasmTarget.Wasm32, true)]
    [InlineData(2, WasmTarget.Wasm32, true)]
    [InlineData(4, WasmTarget.Wasm32, true)]
    [InlineData(1, WasmTarget.Wasm64, true)]
    [InlineData(2, WasmTarget.Wasm64, true)]
    [InlineData(4, WasmTarget.Wasm64, true)]
    public void RejectsRealRuntimeRequirementsBeforeAnyProcess(int required, WasmTarget target, bool reactor)
    {
        var environment = new CompilerCorrectnessEnvironment(
            "unused", "unused", "unused", "unused", "unused", "unused",
            "unused", "unused", "unused", TimeSpan.FromSeconds(1));
        var fixture = CorrectnessTestAssets.CreateFixture("RealRuntimeRequired") with
        {
            RequiredRuntimeCapabilities = (OracleRuntimeCapabilities)required,
            RequiresReactor = reactor,
        };
        var artifact = new CorpusArtifact("unused", "unused", "unused", "unused", "unused", []);
        var compilation = new CorpusCompilation(fixture, CilProfile.Debug, artifact, artifact, "unused");
        var processes = new ExitingCompilerProcessRunner(0);
        var runner = new NetWasmOracleRunner(
            environment, processes, new CorpusCompilerRequestFactory(environment, CorrectnessTestAssets.CreateOracleModes()),
            new OracleOperationProgressReporter(new OracleOperationProgressPlan()),
            new CorpusExportFactory(), new OracleRuntimeCapabilityVerifier(),
            CreateApplicationCompiler(environment, processes));

        var exception = Assert.Throws<InvalidOperationException>(() => runner.CompileAndRun(compilation, target));

        Assert.StartsWith("Oracle runtime lacks required capabilities:", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, processes.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CompilerRequestRetainsAllRealSourcePathsAndNoFictionalEmittedSource(int sourceKind)
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var directory = Directory.CreateTempSubdirectory("netwasm-source-request-");
        try
        {
            var fixture = CorrectnessTestAssets.CreateFixture("SourceMapUnit");
            var artifact = new CorpusArtifact("input.dll", "", "assembly-sha", "", "sdk", []);
            var compilation = new CorpusCompilation(fixture, sourceKind == 1 ? CilProfile.Emitted : CilProfile.Debug,
                artifact, artifact, directory.FullName)
            {
                Sources = sourceKind == 2 ?
                [new("Primary.cs", "primary-source-path", "sha1"), new("Nested/Secondary.cs", "secondary-source-path", "sha2")] : [],
            };
            var processes = new ExitingCompilerProcessRunner(1);
            var runner = new NetWasmOracleRunner(environment, processes, new CorpusCompilerRequestFactory(environment, CorrectnessTestAssets.CreateOracleModes()),
                new OracleOperationProgressReporter(new OracleOperationProgressPlan()), new CorpusExportFactory(),
                new OracleRuntimeCapabilityVerifier(),
                CreateApplicationCompiler(environment, processes));

            Assert.Throws<InvalidOperationException>(() => runner.CompileAndRun(compilation, WasmTarget.Wasm32));

            var request = Assert.Single(processes.Requests);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(request.Arguments[1]));
            var sourcePaths = document.RootElement.GetProperty("SourcePaths").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Equal(sourceKind switch
            {
                0 => [Path.Combine(directory.FullName, "SourceMapUnit.cs")],
                1 => Array.Empty<string>(),
                _ => ["primary-source-path", "secondary-source-path"],
            }, sourcePaths);
            Assert.Equal(1, processes.Calls);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class RecordingCompilerFailures : ICorpusCompilerFailureWriter
    {
        public string Write(CorpusCompilerInvocation invocation, QualifiedProcessResult result, Exception? responseFailure = null) => "/recorded-compiler-failure";
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    public void SuccessfulChildWithInvalidResponseRetainsEvidenceAndNeverRunsModule(string? response)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-response-composition-");
        string? evidence = null;
        try
        {
            var environment = CompilerCorrectnessEnvironment.Discover();
            var input = Path.Combine(directory.FullName, "input.dll");
            File.WriteAllBytes(input, [1, 2, 3]);
            var artifact = new CorpusArtifact(input, "", "fixture-hash", "", "fixture", []);
            var compilation = new CorpusCompilation(CorrectnessTestAssets.CreateFixture("ResponseBoundary"),
                CilProfile.Emitted, artifact, artifact, directory.FullName);
            var processes = new SuccessfulCompilerProcess(response);
            var writer = new CorpusCompilerFailureWriter(new CorpusReplayCommandFormatter());
            var runner = new NetWasmOracleRunner(environment, processes, new CorpusCompilerRequestFactory(environment, CorrectnessTestAssets.CreateOracleModes()),
                new OracleOperationProgressReporter(new OracleOperationProgressPlan()), new CorpusExportFactory(),
                new OracleRuntimeCapabilityVerifier(), CreateApplicationCompiler(environment, processes, failures: writer));

            var failure = Assert.Throws<InvalidOperationException>(() => runner.CompileAndRun(compilation, WasmTarget.Wasm32));

            Assert.Equal(1, processes.Calls);
            if (response is null) Assert.IsType<FileNotFoundException>(failure.InnerException);
            else Assert.IsType<System.Text.Json.JsonException>(failure.InnerException);
            const string marker = "; reproduction: ";
            Assert.Contains(marker, failure.Message, StringComparison.Ordinal);
            evidence = failure.Message[(failure.Message.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "compiler-failure.json")));
            Assert.Equal("compiler-response", json.RootElement.GetProperty("Stage").GetString());
            Assert.Equal(0, json.RootElement.GetProperty("ExitCode").GetInt32());
            Assert.Equal(failure.InnerException!.ToString(), json.RootElement.GetProperty("ResponseFailure").GetString());
            Assert.False(json.RootElement.GetProperty("PartialOutputIsUsable").GetBoolean());
            Assert.Equal(response is not null, File.Exists(Path.Combine(evidence, "response.json")));
            if (response is not null) Assert.Equal(response, File.ReadAllText(Path.Combine(evidence, "response.json")));
            Assert.Empty(failure.Data);
        }
        finally
        {
            directory.Delete(recursive: true);
            if (evidence is not null) Directory.Delete(evidence, recursive: true);
        }
    }

    private sealed class SuccessfulCompilerProcess(string? response) : IQualifiedProcessRunner
    {
        public int Calls { get; private set; }

        public QualifiedProcessResult Run(QualifiedProcessRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (response is not null) File.WriteAllText(request.Arguments[2], response);
            return new(QualifiedProcessCompletion.Exited, 0, "", "", TimeSpan.Zero);
        }
    }

    private sealed class ExitingCompilerProcessRunner(int exitCode) : IQualifiedProcessRunner
    {
        public int Calls { get; private set; }
        public List<QualifiedProcessRequest> Requests { get; } = [];

        public QualifiedProcessResult Run(
            QualifiedProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add(request);
            return new QualifiedProcessResult(
                QualifiedProcessCompletion.Exited,
                Calls == 1 ? exitCode : 0,
                string.Empty,
                string.Empty,
                TimeSpan.Zero);
        }
    }

    private sealed class InfiniteCompilerProcessRunner : IQualifiedProcessRunner
    {
        private readonly QualifiedProcessRunner _processes = new();

        public QualifiedProcessResult Run(
            QualifiedProcessRequest request,
            CancellationToken cancellationToken = default) =>
            _processes.Run(new(
                "/bin/sh",
                ["-c", "trap '' TERM; sleep 30 & wait"],
                request.Timeout), cancellationToken);
    }

    private sealed class RecordingOracleOperationProgressReporter :
        IOracleOperationProgressReporter
    {
        public ConcurrentBag<(int Completed, int Total)> InputReports { get; } = [];

        public ConcurrentBag<(int Completed, int Total)> CompilerReports { get; } = [];

        public void Report(
            string fixtureId,
            WasmTarget target,
            OracleOperationStage stage,
            OracleOperationPlan operationPlan)
        {
        }

        public void ReportExecutionInputs(
            string fixtureId,
            WasmTarget target,
            OracleOperationStage stage,
            int completed,
            int total) => InputReports.Add((completed, total));

        public void ReportCompilerPhases(
            string fixtureId,
            WasmTarget target,
            int completed,
            int total) => CompilerReports.Add((completed, total));

        public void ReportBatchAttempt(
            string fixtureId,
            WasmTarget target,
            OracleOperationStage stage,
            int attempt,
            int observed,
            int total,
            int batchSize)
        {
        }
    }
}
