using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness.TimeZoneInfoFacadeQualification;

internal sealed class TimeZoneInfoFacadeExecutionHarness(
    CompilerCorrectnessEnvironment environment,
    IQualifiedProcessRunner processes)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly CompilerCorrectnessEnvironment _environment =
        environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IQualifiedProcessRunner _processes =
        processes ?? throw new ArgumentNullException(nameof(processes));

    internal TimeZoneInfoFacadeExecution Run(
        CorpusCompilation compilation,
        WasmTarget target,
        TimeZoneInfoFacadeAssetConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(configuration);
        var targetName = target == WasmTarget.Wasm64 ? "wasm64" : "wasm32";
        var modulePath = Path.Combine(
            compilation.Directory,
            $"{compilation.Fixture.Name}.{compilation.Profile}.{targetName}.timezone.wasm");
        var requestPath = modulePath + ".request.json";
        var responsePath = modulePath + ".response.json";
        var hostPath = modulePath + ".host.json";
        var inputsPath = modulePath + ".inputs.json";
        var sourcePath = Path.Combine(
            compilation.Directory,
            compilation.Fixture.Name + ".cs");

        WriteRequest(
            compilation,
            target,
            modulePath,
            requestPath,
            sourcePath);
        WriteHostConfiguration(hostPath, configuration);
        File.WriteAllText(
            inputsPath,
            JsonSerializer.Serialize(compilation.Fixture.Inputs));
        Compile(requestPath, responsePath);
        Validate(modulePath);

        var response = ReadResponse(responsePath);
        var observations = RunNode(
            modulePath,
            inputsPath,
            hostPath,
            response,
            target,
            compilation.Fixture.Inputs);
        return new(observations, modulePath, target);
    }

    private void WriteRequest(
        CorpusCompilation compilation,
        WasmTarget target,
        string modulePath,
        string requestPath,
        string sourcePath)
    {
        var request = new CompilerHostRequest(
            compilation.NetWasm.AssemblyPath,
            [
                _environment.CoreLibPath,
                .. compilation.Fixture.NetWasmReferencePaths,
            ],
            compilation.Fixture.EntryType,
            compilation.Fixture.WasmEntryMethod, [new ExportRequest("run", compilation.Fixture.EntryType, compilation.Fixture.WasmEntryMethod)],
            target,
            null,
            [sourcePath],
            compilation.Fixture.ReferenceAssemblyAliases,
            modulePath,
            compilation.Fixture.CaptureCompilerDiagnostics,
            Path.Combine(_environment.RepositoryRoot, "wit", "netwasm-platform-1.0.0"),
            "netwasm:platform@1.0.0/platform",
            compilation.Fixture.EmitStackTrace,
            null);
        File.WriteAllText(requestPath, JsonSerializer.Serialize(request));
    }

    private static void WriteHostConfiguration(
        string hostPath,
        TimeZoneInfoFacadeAssetConfiguration configuration)
    {
        var hostConfiguration = new
        {
            timeZoneAssetPath = configuration.AssetPath,
            environment = new Dictionary<string, string>
            {
                ["TZ"] = configuration.TimeZone,
            },
        };
        File.WriteAllText(hostPath, JsonSerializer.Serialize(hostConfiguration));
    }

    private void Compile(string requestPath, string responsePath)
    {
        var process = _processes.Run(new(
            _environment.DotNetPath,
            [_environment.CompilerHostPath, requestPath, responsePath],
            _environment.ProcessTimeout));
        if (File.Exists(responsePath))
        {
        }


        if (!process.Succeeded)
        {
            throw new InvalidOperationException(
                "timezone facade compiler host failed");
        }
    }

    private void Validate(string modulePath)
    {
        var process = _processes.Run(new(
            "wasm-tools",
            ["validate", modulePath, "--features", "all"],
            _environment.ProcessTimeout));
        if (!process.Succeeded)
        {
            throw new InvalidOperationException(
                "timezone facade module validation failed");
        }
    }

    private static CompilerHostResponse ReadResponse(string responsePath)
    {
        var response = JsonSerializer.Deserialize<CompilerHostResponse>(
            File.ReadAllText(responsePath),
            JsonOptions);
        return response ?? throw new InvalidOperationException(
            "timezone facade compiler response was empty");
    }

    private ImmutableDictionary<int, OracleObservation> RunNode(
        string modulePath,
        string inputsPath,
        string hostPath,
        CompilerHostResponse response,
        WasmTarget target,
        ImmutableArray<int> inputs)
    {
        var process = _processes.Run(new(
            "node",
            [
                _environment.NodeRunnerPath,
                modulePath,
                "@" + inputsPath,
                target == WasmTarget.Wasm64 ? "wasm64" : "wasm32",
                response.StaticDataEnd.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                hostPath,
            ],
            _environment.ProcessTimeout));
        if (!process.Succeeded)
        {
            throw new InvalidOperationException(
                "timezone facade node execution failed");
        }

        var raw = JsonSerializer.Deserialize<NodeObservation[]>(
            process.StandardOutput,
            JsonOptions);
        if (raw is null)
        {
            throw new InvalidOperationException(
                "timezone facade node response was empty");
        }
        if (raw.Length != inputs.Length)
        {
            throw new InvalidOperationException(
                "timezone facade node response count did not match inputs");
        }

        var observations = ImmutableDictionary.CreateBuilder<int, OracleObservation>();
        for (var index = 0; index < raw.Length; index++)
        {
            var input = inputs[index];
            var observation = raw[index];
            observations.Add(input, ConvertObservation(observation, response.TypeNames));
        }
        return observations.ToImmutable();
    }

    private static OracleObservation ConvertObservation(
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
        return new(kind, raw.Value, exceptionType, raw.Trace);
    }

    private sealed record CompilerHostRequest(
        string EntryAssemblyPath,
        ImmutableArray<string> ReferencePaths,
        string EntryTypeName,
        string EntryMethodName,
        ImmutableArray<ExportRequest> Exports,
        WasmTarget Target,
        string? DiagnosticTracePath,
        ImmutableArray<string> SourcePaths,
        ImmutableDictionary<string, string> ReferenceAssemblyAliases,
        string ModulePath,
        bool CaptureDiagnostic,
        string? WitPath,
        string? WitWorld,
        bool EmitStackTrace,
        string? StackTraceSymbolsPath);

    private sealed record ExportRequest(
        string Name,
        string TypeName,
        string MethodName);

    private sealed record CompilerHostResponse(
        ImmutableDictionary<int, string> TypeNames,
        string ModuleSha256,
        int StaticDataEnd);

    private sealed record NodeObservation(
        string Kind,
        int? Value,
        int? ExceptionTypeId,
        int Trace);
}

internal sealed record TimeZoneInfoFacadeExecution(
    ImmutableDictionary<int, OracleObservation> Observations,
    string ModulePath,
    WasmTarget Target);
