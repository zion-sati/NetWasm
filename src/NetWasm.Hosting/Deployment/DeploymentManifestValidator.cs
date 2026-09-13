using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Hosting.Deployment;

/// <summary>Validates the publish-safe structure and canonical identities of a deployment manifest.</summary>
public sealed class DeploymentManifestValidator : IDeploymentManifestValidator
{
    public void Validate(DeploymentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 1)
        {
            throw new ArgumentException("Unsupported NetWasm deployment manifest schema.", nameof(manifest));
        }

        ValidateDigest(manifest.SemanticBuildId);
        ValidateDigest(manifest.BuildFingerprint);
        if (!Enum.IsDefined(manifest.DeploymentKind))
        {
            throw new ArgumentException("Unsupported NetWasm deployment kind.", nameof(manifest));
        }

        if (!string.Equals(manifest.Profile, "netwasm0.1", StringComparison.Ordinal))
        {
            throw new ArgumentException("Unsupported NetWasm managed profile.", nameof(manifest));
        }

        if (manifest.Target is not ("wasm32" or "wasm64"))
        {
            throw new ArgumentException("Unsupported NetWasm target.", nameof(manifest));
        }

        ValidateText(manifest.FeatureSet);
        ValidateVersionedIdentifier(manifest.ExecutionContract);
        ValidateVersions(manifest.Versions);
        ValidateRuntimeFeatures(manifest.RuntimeFeatures);
        ValidateArtifacts(manifest.Artifacts, manifest.RuntimeFeatures.Contains("local-time", StringComparer.Ordinal));
        ValidateImportModules(manifest.RequiredImportModules, manifest.RequiredImports);
        ValidateFunctions(manifest.RequiredImports);
        ValidateFunctions(manifest.Exports);
    }

    private static void ValidateImportModules(
        ImmutableArray<string> modules,
        ImmutableArray<DeploymentFunction> functions)
    {
        if (modules.IsDefault)
        {
            throw new ArgumentException(
                "Deployment required import modules must be explicit.",
                nameof(modules));
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            ValidateVersionedIdentifier(module);
            if (!identities.Add(module))
            {
                throw new ArgumentException(
                    "Deployment required import modules must be unique.",
                    nameof(modules));
            }
        }

        if (!functions.IsDefault
            && functions.Any(function => function is not null
                && !identities.Contains(function.Interface)))
        {
            throw new ArgumentException(
                "Every required import function must belong to a required import module.",
                nameof(modules));
        }
    }

    private static void ValidateVersions(DeploymentVersions versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ValidateText(versions.Sdk);
        ValidateText(versions.Compiler);
        ValidateText(versions.Runtime);
        ValidateText(versions.RuntimeAbi);
        ValidateText(versions.Hosting);
        ValidateText(versions.Toolchain);
    }

    private static void ValidateRuntimeFeatures(ImmutableArray<string> features)
    {
        if (features.IsDefault)
        {
            throw new ArgumentException("Deployment runtime features must be explicit.", nameof(features));
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in features)
        {
            ValidateToken(feature);
            if (!string.Equals(feature, "local-time", StringComparison.Ordinal))
            {
                throw new ArgumentException("Unsupported deployment runtime feature.", nameof(features));
            }

            if (!identities.Add(feature))
            {
                throw new ArgumentException("Deployment runtime features must be unique.", nameof(features));
            }
        }
    }

    private static void ValidateArtifacts(
        ImmutableArray<DeploymentArtifact> artifacts,
        bool hasLocalTime)
    {
        if (artifacts.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A deployment must contain its application artifact.", nameof(artifacts));
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        var applicationCount = 0;
        DeploymentArtifact? application = null;
        DeploymentArtifact? timeZone = null;
        foreach (var artifact in artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            ValidateRelativePath(artifact.RelativePath);
            if (!paths.Add(artifact.RelativePath))
            {
                throw new ArgumentException("Deployment artifact paths must be unique.", nameof(artifacts));
            }

            ValidateToken(artifact.Role);
            ValidateMediaType(artifact.MediaType);
            ValidateDigest(artifact.Sha256);
            if (artifact.SchemaVersion is <= 0)
            {
                throw new ArgumentException("Subordinate artifact schema versions must be positive.", nameof(artifacts));
            }

            if (string.Equals(artifact.Role, "application", StringComparison.Ordinal))
            {
                applicationCount++;
                application = artifact;
            }
            else if (string.Equals(artifact.Role, "timezone-data", StringComparison.Ordinal))
            {
                if (timeZone is not null)
                {
                    throw new ArgumentException("A deployment may contain only one timezone artifact.", nameof(artifacts));
                }

                timeZone = artifact;
            }
        }

        if (applicationCount != 1)
        {
            throw new ArgumentException("A deployment must identify exactly one application artifact.", nameof(artifacts));
        }

        if (timeZone is null)
        {
            return;
        }

        if (!hasLocalTime)
        {
            throw new ArgumentException("A timezone artifact requires the local-time runtime feature.", nameof(artifacts));
        }

        if (!string.Equals(timeZone.RelativePath, application!.RelativePath + ".tz-info", StringComparison.Ordinal)
            || !string.Equals(timeZone.MediaType, "application/octet-stream", StringComparison.Ordinal)
            || timeZone.SchemaVersion != 1)
        {
            throw new ArgumentException("The timezone artifact must use the adjacent schema-1 deployment contract.", nameof(artifacts));
        }
    }

    private static void ValidateFunctions(ImmutableArray<DeploymentFunction> functions)
    {
        if (functions.IsDefault)
        {
            throw new ArgumentException("Deployment function inventories must be explicit.", nameof(functions));
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            ArgumentNullException.ThrowIfNull(function);
            ValidateVersionedIdentifier(function.Interface);
            ValidateFunctionName(function.Name);
            if (!identities.Add(string.Concat(function.Interface, "\0", function.Name)))
            {
                throw new ArgumentException("Deployment interface functions must be unique.", nameof(functions));
            }

            ValidateSignature(function.Parameters);
            ValidateSignature(function.Results);
        }
    }

    private static void ValidateSignature(ImmutableArray<string> values)
    {
        if (values.IsDefault)
        {
            throw new ArgumentException("Deployment function signatures must be explicit.", nameof(values));
        }

        foreach (var value in values)
        {
            ValidateText(value);
        }
    }

    private static void ValidateDigest(string digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("Deployment identities require lowercase SHA-256 digests.", nameof(digest));
        }
    }

    private static void ValidateRelativePath(string path)
    {
        ValidateText(path);
        if (path[0] == '/' || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Deployment artifact paths must use canonical relative slash syntax.", nameof(path));
        }

        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException("Deployment artifact paths must not contain empty or traversal segments.", nameof(path));
        }
    }

    private static void ValidateMediaType(string mediaType)
    {
        ValidateText(mediaType);
        if (!mediaType.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("Deployment artifact media types must be explicit.", nameof(mediaType));
        }
    }

    private static void ValidateVersionedIdentifier(string value)
    {
        ValidateText(value);
        var separator = value.LastIndexOf('@');
        if (separator <= 0 || separator == value.Length - 1 || value.AsSpan(0, separator).Contains('@'))
        {
            throw new ArgumentException("Deployment contract and interface identities must include one exact version.", nameof(value));
        }

        foreach (var character in value.AsSpan(0, separator))
        {
            if (!(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '.' or ':' or '/'))
            {
                throw new ArgumentException("Deployment contract and interface identities must use canonical lowercase syntax.", nameof(value));
            }
        }

        var versionParts = value[(separator + 1)..].Split('.');
        if (versionParts.Length != 3 || versionParts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)))
        {
            throw new ArgumentException("Deployment contract and interface identities require an exact numeric version.", nameof(value));
        }
    }

    private static void ValidateToken(string value)
    {
        ValidateText(value);
        if (!value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '.'))
        {
            throw new ArgumentException("Deployment tokens must use canonical lowercase syntax.", nameof(value));
        }
    }

    private static void ValidateFunctionName(string value)
    {
        if (!DeploymentFunctionName.IsCanonical(value))
        {
            throw new ArgumentException(
                "Deployment function names must use canonical WIT identity syntax.",
                nameof(value));
        }
    }

    private static void ValidateText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.AsSpan().Trim().Length != value.Length || value.Any(char.IsControl))
        {
            throw new ArgumentException("Deployment text must be canonical and contain no control characters.", nameof(value));
        }
    }
}
