using System;
using System.Text.Json;

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
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, _jsonOptions);
        return new(bytes, manifest);
    }
}
