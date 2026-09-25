using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class NetWasmOracleRunner(
    CompilerCorrectnessEnvironment environment,
    IQualifiedProcessRunner processes,
    ICorpusCompilerRequestFactory compilerRequests,
    IOracleOperationProgressReporter progress,
    ICorpusExportFactory exportFactory,
    IOracleRuntimeCapabilityVerifier capabilities,
    ICorpusApplicationCompiler compiler) : INetWasmOracleRunner
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public NetWasmExecution CompileAndRun(
        CorpusCompilation compilation,
        WasmTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        // This oracle retains allocations and simulates lifetime imports. Reactor
        // support does not turn it into the linked production runtime.
        capabilities.Verify(compilation.Fixture.RequiredRuntimeCapabilities, OracleRuntimeCapabilities.None);
        var targetName = target == WasmTarget.Wasm64 ? "wasm64" : "wasm32";
        var operationPlan = OracleOperationPlan.Create(target, compilation.Fixture);
        var tracePath = compilation.Fixture.CaptureCompilerDiagnostics
            ? Path.Combine(
                compilation.Directory,
                $"{compilation.Fixture.Name}.{compilation.Profile}.{targetName}.trace.txt")
            : null;
        var modulePath = Path.Combine(
            compilation.Directory,
            $"{compilation.Fixture.Name}.{compilation.Profile}.{targetName}.wasm");
        var stackTraceSymbolsPath = compilation.Fixture.EmitStackTrace
            ? modulePath + ".stacktrace.json"
            : null;
        var hostConfigurationPath = stackTraceSymbolsPath is null &&
            compilation.Fixture.ReactorObserveMethod is null
            ? null
            : modulePath + ".host.json";
        var exports = exportFactory.Create(compilation.Fixture);
        progress.Report(compilation.Fixture.Name, target, OracleOperationStage.Compile, operationPlan);
        var response = compiler.Compile(compilation, compilerRequests.Create(
            compilation,
            target,
            tracePath,
            modulePath,
            stackTraceSymbolsPath,
            exports),
            cancellationToken);
        if (hostConfigurationPath is not null)
        {
            File.WriteAllText(hostConfigurationPath, JsonSerializer.Serialize(new
            {
                stackTraceSymbolsPath,
                drainReactor = compilation.Fixture.ReactorObserveMethod is not null,
                observeExportName = compilation.Fixture.ReactorObserveMethod is null
                    ? null
                    : "observe",
            }));
        }
        progress.Report(compilation.Fixture.Name, target, OracleOperationStage.ValidateDirect, operationPlan);
        Validate(modulePath);
        if (target == WasmTarget.Wasm64 && !compilation.Fixture.ExecuteWasm64)
        {
            progress.Report(compilation.Fixture.Name, target, OracleOperationStage.Complete, operationPlan);
            return new(
                ImmutableDictionary<int, OracleObservation>.Empty,
                tracePath,
                modulePath,
                response.ModuleSha256,
                target,
                Executed: false);
        }

        progress.Report(compilation.Fixture.Name, target, OracleOperationStage.ExecuteDirect, operationPlan);
        var observations = RunNodeAll(
            compilation,
            modulePath,
            response.TypeNames,
            response.StaticDataEnd,
            target,
            hostConfigurationPath,
            OracleOperationStage.ExecuteDirect,
            cancellationToken);
        var execution = new NetWasmExecution(
            observations,
            tracePath,
            modulePath,
            response.ModuleSha256,
            target,
            Executed: true);
        if (!compilation.Fixture.ExecuteOptimizedWasm)
        {
            progress.Report(compilation.Fixture.Name, target, OracleOperationStage.Complete, operationPlan);
            return execution;
        }
        progress.Report(compilation.Fixture.Name, target, OracleOperationStage.Optimize, operationPlan);
        var optimized = RunOptimized(
                compilation,
                modulePath,
                response.TypeNames,
                response.StaticDataEnd,
                target,
                hostConfigurationPath,
                operationPlan,
                cancellationToken);
        progress.Report(compilation.Fixture.Name, target, OracleOperationStage.Complete, operationPlan);
        return execution with
        {
            OptimizedObservations = optimized.Observations,
            OptimizedModulePath = optimized.ModulePath,
            OptimizedModuleSha256 = optimized.ModuleSha256,
        };
    }

    private OptimizedExecution RunOptimized(
        CorpusCompilation compilation,
        string modulePath,
        ImmutableDictionary<int, string> typeNames,
        int staticDataEnd,
        WasmTarget target,
        string? hostConfigurationPath,
        OracleOperationPlan operationPlan,
        CancellationToken cancellationToken)
    {
        var optimizedPath = Path.ChangeExtension(modulePath, ".optimized.wasm");
        var optimization = processes.Run(new(
            "wasm-opt",
            [
                modulePath,
                "-O",
                "--enable-exception-handling",
                "--enable-memory64",
                "--enable-bulk-memory",
                "--enable-nontrapping-float-to-int",
                "-o",
                optimizedPath,
            ],
            environment.ProcessTimeout), cancellationToken);
        if (!optimization.Succeeded)
        {
            throw new InvalidOperationException(
                "wasm-opt rejected compiler output: " +
                $"completion={optimization.Completion}, exit={optimization.ExitCode}, " +
                optimization.StandardOutput + optimization.StandardError,
                optimization.LaunchException);
        }
        progress.Report(compilation.Fixture.Name, target, OracleOperationStage.ValidateOptimized, operationPlan);
        Validate(optimizedPath);
        progress.Report(compilation.Fixture.Name, target, OracleOperationStage.ExecuteOptimized, operationPlan);
        var observations = RunNodeAll(
            compilation,
            optimizedPath,
            typeNames,
            staticDataEnd,
            target,
            hostConfigurationPath,
            OracleOperationStage.ExecuteOptimized,
            cancellationToken);
        return new(
            optimizedPath,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(optimizedPath))),
            observations);
    }

    private ImmutableDictionary<int, OracleObservation> RunNodeAll(
        CorpusCompilation compilation,
        string modulePath,
        ImmutableDictionary<int, string> typeNames,
        int staticDataEnd,
        WasmTarget target,
        string? hostConfigurationPath,
        OracleOperationStage executionStage,
        CancellationToken cancellationToken)
    {
        if (!compilation.Fixture.SupportsBatchedOracle)
        {
            return compilation.Fixture.Inputs.ToImmutableDictionary(
                input => input,
                input => RunNode(
                    modulePath,
                    input,
                    typeNames,
                    staticDataEnd,
                    target,
                    hostConfigurationPath,
                    cancellationToken));
        }

        var inputs = compilation.Fixture.Inputs;
        var observations = ImmutableDictionary.CreateBuilder<int, OracleObservation>();
        var batchSequence = 0;
        var completed = 0;
        var reported = 0;
        progress.ReportExecutionInputs(
            compilation.Fixture.Name,
            target,
            executionStage,
            completed,
            inputs.Length);
        foreach (var batch in OracleInputBatching.Partition(inputs))
        {
            RunBatchChunk(batch);
        }
        return observations.ToImmutable();

        void RunBatchChunk(ImmutableArray<int> batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = ++batchSequence;
            var inputPath = $"{modulePath}.inputs-{attempt - 1:D4}.json";
            File.WriteAllText(
                inputPath,
                JsonSerializer.Serialize(batch, SerializerOptions));
            progress.ReportBatchAttempt(
                compilation.Fixture.Name,
                target,
                executionStage,
                attempt,
                reported,
                inputs.Length,
                batch.Length);
            var request = new QualifiedProcessRequest(
                "node",
                [
                    environment.NodeRunnerPath,
                modulePath,
                    "@" + inputPath,
                    target == WasmTarget.Wasm64 ? "wasm64" : "wasm32",
                    staticDataEnd.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    .. hostConfigurationPath is null
                        ? Array.Empty<string>()
                        : new[] { hostConfigurationPath },
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
                    Detail = $"NetWasm oracle batch exceeded {environment.ProcessTimeout}",
                    TraceRecords = [],
                };
                observations.Add(batch[0], timeout);
                completed++;
                ReportObserved(completed);
                return;
            }
            if (!process.Succeeded)
            {
                throw new InvalidOperationException(
                    "NetWasm oracle batch process failed: " +
                    $"completion={process.Completion}, exit={process.ExitCode}",
                    process.LaunchException);
            }

            var raw = JsonSerializer.Deserialize<NodeObservation[]>(
            process.StandardOutput, SerializerOptions)
            ?? throw new InvalidOperationException(
                "NetWasm oracle batch returned no observations");
            if (raw.Length != batch.Length)
            {
                throw new InvalidOperationException(
                    $"NetWasm oracle batch returned {raw.Length} observations " +
                        $"for {batch.Length} inputs");
            }

            for (var index = 0; index < raw.Length; index++)
            {
                observations.Add(
                    batch[index],
                    ConvertNodeObservation(raw[index], typeNames));
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
            progress.ReportExecutionInputs(
                compilation.Fixture.Name,
                target,
                executionStage,
                reported,
                inputs.Length);
        }
    }

    private OracleObservation RunNode(
        string modulePath,
        int input,
        ImmutableDictionary<int, string> typeNames,
        int staticDataEnd,
        WasmTarget target,
        string? hostConfigurationPath,
        CancellationToken cancellationToken)
    {
        var process = processes.Run(new(
            "node",
            [
                environment.NodeRunnerPath,
                modulePath,
                input.ToString(System.Globalization.CultureInfo.InvariantCulture),
                target == WasmTarget.Wasm64 ? "wasm64" : "wasm32",
                staticDataEnd.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                .. hostConfigurationPath is null
                    ? Array.Empty<string>()
                    : new[] { hostConfigurationPath },
            ],
            environment.ProcessTimeout), cancellationToken);
        if (process.Completion == QualifiedProcessCompletion.TimedOut)
        {
            return new(OracleObservationKind.TimedOut, null, null, 0)
            {
                Detail = $"NetWasm oracle exceeded {environment.ProcessTimeout}",
                TraceRecords = [],
            };
        }
        if (!process.Succeeded)
        {
            throw new InvalidOperationException(
                "NetWasm oracle process failed: " +
                $"completion={process.Completion}, exit={process.ExitCode}, " +
                process.StandardOutput + process.StandardError,
                process.LaunchException);
        }
        var raw = JsonSerializer.Deserialize<NodeObservation>(
            process.StandardOutput, SerializerOptions)
            ?? throw new InvalidOperationException("NetWasm oracle returned no observation");
        return ConvertNodeObservation(raw, typeNames);
    }

    private static OracleObservation ConvertNodeObservation(
        NodeObservation raw,
        ImmutableDictionary<int, string> typeNames)
    {
        var kind = raw.Kind switch
        {
            "value" => OracleObservationKind.Value,
            "exception" => OracleObservationKind.ManagedException,
            _ => OracleObservationKind.Trap,
        };
        var exceptionType = raw.ExceptionTypeId is { } typeId &&
            typeNames.TryGetValue(typeId, out var name)
                ? name
                : null;
        return new OracleObservation(kind, raw.Value, exceptionType, raw.Trace)
        {
            Detail = string.Join("; ", new[]
            {
                raw.Detail,
                raw.ManagedMessage is null
                    ? null
                    : $"managedMessage={raw.ManagedMessage}",
                raw.ManagedStackTrace is null
                    ? null
                    : $"managedStackTrace={raw.ManagedStackTrace}",
            }.Where(part => part is not null)),
            TraceRecords = raw.TraceRecords is { Length: > 0 }
                ? raw.TraceRecords.Select(record => new TraceRecord(
                    (TraceRecordKind)record.Kind,
                    record.EventId,
                    unchecked(((long)record.PayloadHigh << 32) |
                        (uint)record.PayloadLow))).ToImmutableArray()
                : [new(TraceRecordKind.StateChecksum, 0, raw.Trace)],
        };
    }

    private void Validate(string modulePath)
    {
        var process = processes.Run(new(
            "wasm-tools",
            ["validate", modulePath, "--features", "all"],
            environment.ProcessTimeout));
        if (!process.Succeeded)
        {
            throw new InvalidOperationException(
                "wasm-tools rejected compiler output: " +
                $"completion={process.Completion}, exit={process.ExitCode}, " +
                process.StandardOutput + process.StandardError,
                process.LaunchException);
        }
    }


    private sealed record NodeObservation(
        string Kind,
        int? Value,
        int? ExceptionTypeId,
        int Trace,
        string? Detail,
        NodeTraceRecord[]? TraceRecords,
        string? ManagedMessage,
        string? ManagedStackTrace);

    private sealed record NodeTraceRecord(
        int Kind,
        int EventId,
        int PayloadLow,
        int PayloadHigh);

    private sealed record OptimizedExecution(
        string ModulePath,
        string ModuleSha256,
        ImmutableDictionary<int, OracleObservation> Observations);
}
