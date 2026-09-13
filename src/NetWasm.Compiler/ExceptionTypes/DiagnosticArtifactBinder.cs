using System;
using System.Linq;

namespace NetWasm.Compiler.ExceptionTypes;

public interface IDiagnosticArtifactBinder
{
    DiagnosticArtifactBundle Bind(
        byte[] finalWasm,
        ExceptionTypeMapArtifact map,
        string buildId);
}

public sealed class DiagnosticArtifactBinder(
    IArtifactDigestCalculator digests,
    IExceptionTypeMapIntegrityVerifier mapIntegrity,
    IDiagnosticArtifactManifestWriter manifests) : IDiagnosticArtifactBinder
{
    private readonly IArtifactDigestCalculator _digests = digests ??
        throw new ArgumentNullException(nameof(digests));
    private readonly IExceptionTypeMapIntegrityVerifier _mapIntegrity = mapIntegrity ??
        throw new ArgumentNullException(nameof(mapIntegrity));
    private readonly IDiagnosticArtifactManifestWriter _manifests = manifests ??
        throw new ArgumentNullException(nameof(manifests));

    public DiagnosticArtifactBundle Bind(
        byte[] finalWasm,
        ExceptionTypeMapArtifact map,
        string buildId)
    {
        ArgumentNullException.ThrowIfNull(finalWasm);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);

        var boundWasm = finalWasm.ToArray();
        var boundMapBytes = map.Bytes.ToArray();
        var boundMap = map with { Bytes = boundMapBytes };
        _mapIntegrity.Verify(boundMap);
        var manifest = _manifests.Write(
            buildId,
            _digests.Calculate(boundWasm),
            boundMap.Sha256);
        return new(boundWasm, boundMap, manifest.Bytes, manifest.Manifest);
    }
}
