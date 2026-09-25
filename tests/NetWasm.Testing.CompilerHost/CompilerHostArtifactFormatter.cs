using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Testing.CompilerHost;

internal sealed record CompilerHostArtifact(string Path, ImmutableArray<byte> Bytes);

internal interface ICompilerHostArtifactFormatter
{
    ImmutableArray<CompilerHostArtifact> Format(CompilationRequest request, CompilationResult result);
}

internal sealed class CompilerHostArtifactFormatter : ICompilerHostArtifactFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    public ImmutableArray<CompilerHostArtifact> Format(CompilationRequest request, CompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        var artifacts = ImmutableArray.CreateBuilder<CompilerHostArtifact>();
        Add(request.ModulePath, result.ApplicationModule);
        if (request.EmitStackTrace)
        {
            Add(request.StackTraceSymbolsPath, result.StackTraceSymbols?.Bytes ??
                throw new InvalidOperationException("instrumented compilation produced no stack-trace sidecar"));
        }
        if (request.RuntimeLayoutPath is not null)
        {
            var target = request.Target switch
            {
                WasmTarget.Wasm32 => "wasm32",
                WasmTarget.Wasm64 => "wasm64",
                _ => throw new ArgumentOutOfRangeException(nameof(request), "unsupported runtime target"),
            };
            if (result.StaticDataEnd < 0)
                throw new InvalidOperationException("application static-data extent is negative");
            Add(request.RuntimeLayoutPath, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 2,
                target,
                applicationStaticDataEnd = result.StaticDataEnd,
            }, JsonOptions));
        }
        if (request.InteropManifestPath is not null)
        {
            ArgumentNullException.ThrowIfNull(result.InteropManifest);
            Add(request.InteropManifestPath, JsonSerializer.SerializeToUtf8Bytes(result.InteropManifest, JsonOptions));
        }
        return artifacts.ToImmutable();

        void Add(string? path, byte[] bytes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(bytes);
            if (artifacts.Any(artifact => StringComparer.Ordinal.Equals(artifact.Path, path)))
                throw new ArgumentException("compiler artifact paths must be distinct", nameof(request));
            artifacts.Add(new(path, [.. bytes]));
        }
    }
}
