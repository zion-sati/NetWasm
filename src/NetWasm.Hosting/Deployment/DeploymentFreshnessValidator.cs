using System;
using System.Collections.Generic;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Deployment;

/// <summary>Binds a local descriptor to exact manifest bytes and the complete observed artifact closure.</summary>
public sealed class DeploymentFreshnessValidator : IDeploymentFreshnessValidator
{
    private readonly IExecutionDescriptorValidator _descriptorValidator;
    private readonly IDeploymentManifestReader _manifestReader;
    private readonly IContentHasher _contentHasher;

    public DeploymentFreshnessValidator(
        IExecutionDescriptorValidator descriptorValidator,
        IDeploymentManifestReader manifestReader,
        IContentHasher contentHasher)
    {
        _descriptorValidator = descriptorValidator ?? throw new ArgumentNullException(nameof(descriptorValidator));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
        _contentHasher = contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));
    }

    public DeploymentManifest Validate(DeploymentFreshnessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _descriptorValidator.Validate(request.ExecutionDescriptor);
        if (request.ManifestUtf8.IsEmpty)
        {
            throw new ArgumentException("Deployment manifest bytes are required.", nameof(request));
        }

        var manifest = _manifestReader.Read(request.ManifestUtf8);
        if (!string.Equals(
                request.ExecutionDescriptor.DeploymentManifestSha256,
                _contentHasher.Hash(request.ManifestUtf8),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Deployment manifest bytes do not match the execution descriptor.", nameof(request));
        }

        if (!string.Equals(request.ExecutionDescriptor.BuildFingerprint, manifest.BuildFingerprint, StringComparison.Ordinal))
        {
            throw new ArgumentException("The execution descriptor names a stale source/build fingerprint.", nameof(request));
        }

        if (!string.Equals(request.ExecutionDescriptor.HostingVersion, manifest.Versions.Hosting, StringComparison.Ordinal))
        {
            throw new ArgumentException("The execution descriptor names a different Hosting version.", nameof(request));
        }

        if (request.Artifacts.IsDefault)
        {
            throw new ArgumentException("Observed deployment artifacts must be explicit.", nameof(request));
        }

        var observed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var artifact in request.Artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            ArgumentException.ThrowIfNullOrWhiteSpace(artifact.RelativePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(artifact.Sha256);
            if (!observed.TryAdd(artifact.RelativePath, artifact.Sha256))
            {
                throw new ArgumentException("Observed deployment artifact paths must be unique.", nameof(request));
            }
        }

        if (observed.Count != manifest.Artifacts.Length)
        {
            throw new ArgumentException("The observed deployment artifact closure is incomplete or contains extra files.", nameof(request));
        }

        foreach (var artifact in manifest.Artifacts)
        {
            if (!observed.TryGetValue(artifact.RelativePath, out var digest)
                || !string.Equals(artifact.Sha256, digest, StringComparison.Ordinal))
            {
                throw new ArgumentException("A deployment artifact is missing or has changed.", nameof(request));
            }
        }

        return manifest;
    }
}
