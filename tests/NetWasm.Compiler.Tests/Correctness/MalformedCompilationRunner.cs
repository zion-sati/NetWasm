using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class MalformedCompilationRunner(
    CompilerCorrectnessEnvironment environment,
    IQualifiedProcessRunner processes) : IMalformedCompilationRunner
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public MalformedCompilationObservation Compile(
        MalformedInputMutation mutation,
        string referencePath,
        string entryType,
        string outputDirectory,
        int attempt)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentException.ThrowIfNullOrWhiteSpace(referencePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryType);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        Directory.CreateDirectory(outputDirectory);
        var prefix = Path.Combine(outputDirectory, "malformed-" + Guid.NewGuid().ToString("N"));
        var requestPath = prefix + ".request.json";
        var responsePath = prefix + ".response.json";
        var modulePath = prefix + ".wasm";
        var tracePath = prefix + ".trace";
        using (var requestFile = new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(requestFile, new
            {
                EntryAssemblyPath = mutation.Path,
                ReferencePaths = ImmutableArray.Create(referencePath),
                EntryTypeName = entryType,
                EntryMethodName = "Run",
                Exports = ImmutableArray<RequestedExport>.Empty,
                Target = WasmTarget.Wasm32,
                DiagnosticTracePath = tracePath,
                SourcePaths = ImmutableArray<string>.Empty,
                ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty,
                ModulePath = modulePath,
                CaptureDiagnostic = true,
            });
        }
        var process = processes.Run(new(
            environment.DotNetPath,
            [environment.CompilerHostPath, requestPath, responsePath],
            environment.ProcessTimeout));
        CapturedDiagnostic? captured = null;
        Exception? responseFailure = null;
        if (process.Succeeded && File.Exists(responsePath))
        {
            try
            {
                captured = JsonSerializer.Deserialize<CapturedDiagnostic>(
                    File.ReadAllText(responsePath),
                    SerializerOptions);
            }
            catch (Exception exception)
            {
                responseFailure = exception;
            }
        }
        return new(
            process.Completion,
            captured is null
                ? null
                : new(
                    (DiagnosticCode)captured.Code,
                    captured.Message,
                    captured.Method,
                    captured.IlOffset),
            File.Exists(modulePath),
            File.Exists(tracePath + ".passes/failure.json"),
            process.StandardError)
        {
            ExitCode = process.ExitCode,
            ArtifactPrefix = prefix,
            Process = process,
            ResponseFailure = responseFailure,
        };
    }

    private sealed record CapturedDiagnostic(
        int Code,
        string Message,
        string? Method,
        int? IlOffset);
}
