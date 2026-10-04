using System.Text.Json;
using System.Text.Json.Serialization;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class CompilerArtifactWriter(
    INativeCallbackSupportArtifactValidator callbackSupport) : ICompilerArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    private readonly INativeCallbackSupportArtifactValidator _callbackSupport =
        callbackSupport ?? throw new ArgumentNullException(nameof(callbackSupport));

    public void Write(CompilerArtifactWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var compilation = request.Compilation;
        if (compilation.NativeImports.IsDefault)
            throw new InvalidOperationException("The compiler native import facts are uninitialized.");
        _callbackSupport.Validate(compilation.NativeCallbackSupport);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NativeCallbackObjectPath);
        var runtimeFeatures = compilation.RuntimeFeatures.IsDefault
            ? []
            : compilation.RuntimeFeatures
                .Order(StringComparer.Ordinal)
                .ToArray();
        WriteBytes(request.CoreModulePath, compilation.CoreModule);
        if (compilation.NativeCallbackSupport is { } callbackSupport)
        {
            WriteBytes(request.NativeCallbackObjectPath, callbackSupport.ObjectBytes);
            WriteJson(request.RuntimeLayoutPath, new
            {
                schemaVersion = 4,
                target = compilation.Target,
                applicationStaticDataEnd = compilation.StaticDataEnd,
                managedExecutableEntryPoint = compilation.EntryPoint,
                runtimeFeatures,
                nativeImports = compilation.NativeImports,
                nativeCallbackSupport = new
                {
                    fileName = Path.GetFileName(request.NativeCallbackObjectPath),
                    callbackSupport.Sha256,
                    callbackSupport.Callbacks,
                    callbackSupport.TemporaryApplicationExports,
                    callbackSupport.TemporaryRuntimeExports,
                },
            });
        }
        else
        {
            File.Delete(request.NativeCallbackObjectPath);
            WriteJson(request.RuntimeLayoutPath, new
            {
                schemaVersion = 3,
                target = compilation.Target,
                applicationStaticDataEnd = compilation.StaticDataEnd,
                managedExecutableEntryPoint = compilation.EntryPoint,
                runtimeFeatures,
                nativeImports = compilation.NativeImports,
            });
        }
        WriteJson(request.InteropManifestPath, compilation.InteropManifest);
        if (request.ExceptionTypeMapPath is not null)
        {
            var map = compilation.ExceptionTypeMap ??
                throw new InvalidOperationException(
                    "compiler diagnostics did not produce an exception type map");
            WriteBytes(request.ExceptionTypeMapPath, map.Bytes);
        }
        var functionImports = compilation.FunctionImports.IsDefault
            ? []
            : compilation.FunctionImports;
        WriteJson(request.CompilerMetadataPath, new
        {
            schemaVersion = 1,
            target = compilation.Target,
            runtimeFeatures,
            functionImports = functionImports.Select(import => new
            {
                import.Module,
                import.Name,
                parameters = import.Type.Parameters,
                result = import.Type.Result,
            }).ToArray(),
        });
        if (request.StackTraceSymbolsPath is not null)
        {
            var symbols = compilation.StackTraceSymbols ??
                throw new InvalidOperationException(
                    "stack-trace instrumentation did not produce its symbol sidecar");
            WriteBytes(request.StackTraceSymbolsPath, symbols.Bytes);
        }
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }
}
