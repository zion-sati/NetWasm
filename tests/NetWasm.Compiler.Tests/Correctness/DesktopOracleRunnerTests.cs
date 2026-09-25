using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class DesktopOracleRunnerTests
{
    [Fact]
    public void RunRecordsExactValuesManagedExceptionTypesAndTraces()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var compilation = new RoslynCorpusCompiler(
            environment,
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier())).Compile(
            CorrectnessTestAssets.CreateFixture("DesktopOracleRunnerUnit"),
            CilProfile.Release,
            CorrectnessTestAssets.CreateDirectory());

        var observations = new DesktopOracleRunner(
            environment,
            new QualifiedProcessRunner(),
            new DesktopOracleProgressReporter(TextWriter.Null)).Run(compilation);

        Assert.Equal(OracleObservationKind.Value, observations[3].Kind);
        Assert.Equal(4, observations[3].Value);
        Assert.Null(observations[3].ExceptionType);
        Assert.Equal(6, observations[3].Trace);
        Assert.Equal(
            new TraceRecord(TraceRecordKind.StateChecksum, 0, 6),
            Assert.Single(observations[3].TraceRecords));
        Assert.Equal(OracleObservationKind.ManagedException, observations[-1].Kind);
        Assert.Equal("System.ArgumentOutOfRangeException", observations[-1].ExceptionType);
        Assert.Equal(-2, observations[-1].Trace);
    }

    [Fact]
    public void BatchedProtocolMatchesIsolatedPerInputObservations()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var compilation = new RoslynCorpusCompiler(
            environment,
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier())).Compile(
            CorrectnessTestAssets.CreateFixture("DesktopOracleBatchUnit"),
            CilProfile.Release,
            CorrectnessTestAssets.CreateDirectory());
        var runner = new DesktopOracleRunner(
            environment,
            new QualifiedProcessRunner(),
            new DesktopOracleProgressReporter(TextWriter.Null));

        var isolated = runner.Run(compilation);
        var progress = new RecordingDesktopProgressReporter();
        var batched = new DesktopOracleRunner(
            environment,
            new QualifiedProcessRunner(),
            progress).Run(compilation with
            {
                Fixture = compilation.Fixture with
                {
                    SupportsBatchedOracle = true,
                },
            });

        Assert.Equal(isolated.Keys.Order(), batched.Keys.Order());
        foreach (var input in isolated.Keys)
        {
            Assert.Equal(isolated[input].Kind, batched[input].Kind);
            Assert.Equal(isolated[input].Value, batched[input].Value);
            Assert.Equal(isolated[input].ExceptionType, batched[input].ExceptionType);
            Assert.Equal(isolated[input].Trace, batched[input].Trace);
            Assert.Equal(isolated[input].Detail, batched[input].Detail);
            Assert.Equal(
                isolated[input].TraceRecords.AsEnumerable(),
                batched[input].TraceRecords.AsEnumerable());
        }
        Assert.Contains(progress.Reports, report => report.Completed == 1);
    }

    [Fact]
    public void BatchedProtocolUsesReportedProgressToLocalizeTimedOutChunk()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var compilation = new RoslynCorpusCompiler(
            environment,
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier())).Compile(
            CorrectnessTestAssets.CreateFixture("DesktopOracleAdaptiveBatchUnit"),
            CilProfile.Release,
            CorrectnessTestAssets.CreateDirectory());
        var inputs = Enumerable.Range(0, 101).ToImmutableArray();
        compilation = compilation with
        {
            Fixture = compilation.Fixture with
            {
                Inputs = inputs,
                SupportsBatchedOracle = true,
            },
        };
        var processes = new TimeoutLargestBatchProcessRunner();

        var observations = new DesktopOracleRunner(
            environment,
            processes,
            new DesktopOracleProgressReporter(TextWriter.Null)).Run(compilation);

        Assert.Equal(inputs, observations.Keys.Order());
        Assert.Contains(100, processes.BatchLengths);
        Assert.Contains(96, processes.BatchLengths);
        Assert.Contains(4, processes.BatchLengths);
        Assert.All(processes.BatchLengths, length =>
            Assert.InRange(length, 1, OracleInputBatching.MaxInputsPerProcess));
    }

    private sealed class TimeoutLargestBatchProcessRunner : IQualifiedProcessRunner
    {
        public ConcurrentBag<int> BatchLengths { get; } = [];

        public QualifiedProcessResult Run(
            QualifiedProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            var inputPath = request.Arguments[4][1..];
            var inputs = JsonSerializer.Deserialize<int[]>(File.ReadAllText(inputPath))!;
            BatchLengths.Add(inputs.Length);
            if (inputs.Length == OracleInputBatching.MaxInputsPerProcess)
            {
                request.Progress?.Invoke(96, inputs.Length);
                return new(
                    QualifiedProcessCompletion.TimedOut,
                    null,
                    string.Empty,
                    string.Empty,
                    TimeSpan.FromSeconds(1));
            }

            var output = JsonSerializer.Serialize(inputs.Select(input => new
            {
                kind = "value",
                value = input,
                exceptionType = (string?)null,
                trace = input,
                detail = (string?)null,
                traceRecords = Array.Empty<object>(),
            }));
            return new(
                QualifiedProcessCompletion.Exited,
                0,
                output,
                string.Empty,
                TimeSpan.FromMilliseconds(1));
        }
    }

    private sealed class RecordingDesktopProgressReporter :
        IDesktopOracleProgressReporter
    {
        public ConcurrentBag<(int Completed, int Total)> Reports { get; } = [];

        public void Report(string fixtureId, int completed, int total) =>
            Reports.Add((completed, total));

        public void ReportBatchAttempt(
            string fixtureId,
            int attempt,
            int observed,
            int total,
            int batchSize)
        {
        }
    }
}
