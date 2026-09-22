using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.ExceptionTypes;

public sealed record DiagnosticArtifactManifestArtifact(
    byte[] Bytes,
    DiagnosticArtifactBindingManifest Manifest);

public interface IDiagnosticArtifactManifestWriter
{
    DiagnosticArtifactManifestArtifact Write(
        string buildId,
        string wasmSha256,
        string exceptionTypeMapSha256);
}

public sealed class DiagnosticArtifactManifestWriter :
    IDiagnosticArtifactManifestWriter
{
    public DiagnosticArtifactManifestArtifact Write(
        string buildId,
        string wasmSha256,
        string exceptionTypeMapSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(wasmSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(exceptionTypeMapSha256);

        var manifest = new DiagnosticArtifactBindingManifest(
            1,
            buildId,
            wasmSha256,
            exceptionTypeMapSha256);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            DiagnosticArtifactJsonContext.Default.DiagnosticArtifactBindingManifest);
        return new(bytes, manifest);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DiagnosticArtifactBindingManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class DiagnosticArtifactJsonContext : JsonSerializerContext;
