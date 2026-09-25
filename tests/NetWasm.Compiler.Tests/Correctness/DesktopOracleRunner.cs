using System.Collections.Immutable;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class DesktopOracleRunner(
    CompilerCorrectnessEnvironment environment,
    IQualifiedProcessRunner processes,
    IDesktopOracleProgressReporter progress) : IDesktopOracleRunner
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ImmutableDictionary<int, OracleObservation> Run(
        CorpusCompilation compilation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (compilation.Fixture.SupportsBatchedOracle)
        {
            return RunBatch(compilation, cancellationToken);
        }

        var result = ImmutableDictionary.CreateBuilder<int, OracleObservation>();
        var completed = 0;
        progress.Report(compilation.Fixture.Name, completed, compilation.Fixture.Inputs.Length);
        foreach (var input in compilation.Fixture.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(input, Observe(compilation, input, cancellationToken));
            completed++;
            progress.Report(compilation.Fixture.Name, completed, compilation.Fixture.Inputs.Length);
        }
        return result.ToImmutable();
    }

    private ImmutableDictionary<int, OracleObservation> RunBatch(
        CorpusCompilation compilation,
        CancellationToken cancellationToken)
    {
        var inputs = compilation.Fixture.Inputs;
        var result = ImmutableDictionary.CreateBuilder<int, OracleObservation>();
        var batchSequence = 0;
        var completed = 0;
        var reported = 0;
        progress.Report(compilation.Fixture.Name, 0, inputs.Length);
        foreach (var batch in OracleInputBatching.Partition(inputs))
        {
            RunBatchChunk(batch);
        }
        return result.ToImmutable();

        void RunBatchChunk(ImmutableArray<int> batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = ++batchSequence;
            var inputPath = Path.Combine(
                compilation.Directory,
                $"desktop-oracle-inputs-{attempt - 1:D4}.json");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(batch, SerializerOptions));
            progress.ReportBatchAttempt(
                compilation.Fixture.Name,
                attempt,
                reported,
                inputs.Length,
                batch.Length);
            var request = new QualifiedProcessRequest(
                environment.DotNetPath,
                [
                environment.DesktopOracleHostPath,
                compilation.Desktop.AssemblyPath,
                compilation.Fixture.EntryType,
                compilation.Fixture.DesktopEntryMethod,
                "@" + inputPath,
                compilation.Fixture.UsesTypedTrace.ToString(),
                ],
                environment.ProcessTimeout)
            {
                Progress = (childCompleted, _) =>
                    ReportObserved(completed + childCompleted),
            };
            var process = processes.Run(request, cancellationToken);
            if (process.Completion == QualifiedProcessCompletion.TimedOut)
            {
                if (batch.Length > 1)
                {
                    var (left, right) =
                        OracleProgressDirectedBatching.Split(
                            batch,
                            Math.Clamp(
                                reported - completed,
                                0,
                                batch.Length));
                    RunBatchChunk(left);
                    RunBatchChunk(right);
                    return;
                }
                var timeout = new OracleObservation(
                    OracleObservationKind.TimedOut, null, null, 0)
                {
                    Detail = $"desktop oracle batch exceeded {environment.ProcessTimeout}",
                    TraceRecords = [],
                };
                result.Add(batch[0], timeout);
                completed++;
                ReportObserved(completed);
                return;
            }
            if (!process.Succeeded)
            {
                throw new InvalidOperationException(
                    "desktop oracle batch process failed: " +
                    $"completion={process.Completion}, exit={process.ExitCode}",
                    process.LaunchException);
            }

            var observations = JsonSerializer.Deserialize<HostObservation[]>(
                process.StandardOutput, SerializerOptions)
                ?? throw new InvalidOperationException(
                    "desktop oracle batch returned no observations");
            if (observations.Length != batch.Length)
            {
                throw new InvalidOperationException(
                    $"desktop oracle batch returned {observations.Length} observations " +
                        $"for {batch.Length} inputs");
            }

            for (var index = 0; index < batch.Length; index++)
            {
                result.Add(batch[index], Convert(observations[index]));
            }
            completed += batch.Length;
            ReportObserved(completed);
        }

        void ReportObserved(int candidate)
        {
            candidate = Math.Min(candidate, inputs.Length);
            if (candidate <= reported)
            {
                return;
            }
            reported = candidate;
            progress.Report(compilation.Fixture.Name, reported, inputs.Length);
        }
    }

    private OracleObservation Observe(
        CorpusCompilation compilation,
        int input,
        CancellationToken cancellationToken)
    {
        var process = processes.Run(new(
            environment.DotNetPath,
            [
                environment.DesktopOracleHostPath,
                compilation.Desktop.AssemblyPath,
                compilation.Fixture.EntryType,
                compilation.Fixture.DesktopEntryMethod,
                input.ToString(System.Globalization.CultureInfo.InvariantCulture),
                compilation.Fixture.UsesTypedTrace.ToString(),
            ],
            environment.ProcessTimeout), cancellationToken);
        if (process.Completion == QualifiedProcessCompletion.TimedOut)
        {
            return new(OracleObservationKind.TimedOut, null, null, 0)
            {
                Detail = $"desktop oracle exceeded {environment.ProcessTimeout}",
                TraceRecords = [],
            };
        }
        if (!process.Succeeded)
        {
            throw new InvalidOperationException(
                "desktop oracle process failed: " +
                $"completion={process.Completion}, exit={process.ExitCode}, " +
                process.StandardOutput + process.StandardError,
                process.LaunchException);
        }

        var raw = JsonSerializer.Deserialize<HostObservation>(
            process.StandardOutput, SerializerOptions)
            ?? throw new InvalidOperationException(
                "desktop oracle returned no observation");
        return Convert(raw);
    }

    private static OracleObservation Convert(HostObservation raw)
    {
        var kind = raw.Kind switch
        {
            "value" => OracleObservationKind.Value,
            "exception" => OracleObservationKind.ManagedException,
            _ => throw new InvalidOperationException(
                $"desktop oracle returned unknown kind '{raw.Kind}'"),
        };
        return new(kind, raw.Value, raw.ExceptionType, raw.Trace)
        {
            Detail = raw.Detail,
            TraceRecords = raw.TraceRecords.Select(record => new TraceRecord(
                (TraceRecordKind)record.Kind,
                record.EventId,
                unchecked(((long)record.PayloadHigh << 32) |
                    (uint)record.PayloadLow))).ToImmutableArray(),
        };
    }

    private sealed record HostObservation(
        string Kind,
        int? Value,
        string? ExceptionType,
        int Trace,
        string? Detail,
        HostTraceRecord[] TraceRecords);

    private sealed record HostTraceRecord(
        int Kind,
        int EventId,
        int PayloadLow,
        int PayloadHigh);
}
