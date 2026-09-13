using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class CompilerArtifactWriter : ICompilerArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public void Write(CompilerArtifactWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var compilation = request.Compilation;
        WriteBytes(request.CoreModulePath, compilation.CoreModule);
        WriteJson(request.RuntimeLayoutPath, new
        {
            schemaVersion = 2,
            target = compilation.Target,
            applicationStaticDataEnd = compilation.StaticDataEnd,
            managedExecutableEntryPoint = compilation.EntryPoint,
        });
        WriteJson(request.InteropManifestPath, compilation.InteropManifest);
        var runtimeFeatures = compilation.RuntimeFeatures.IsDefault
            ? []
            : compilation.RuntimeFeatures;
        var functionImports = compilation.FunctionImports.IsDefault
            ? []
            : compilation.FunctionImports;
        WriteJson(request.CompilerMetadataPath, new
        {
            schemaVersion = 1,
            target = compilation.Target,
            runtimeFeatures = runtimeFeatures
                .Order(StringComparer.Ordinal)
                .ToArray(),
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
