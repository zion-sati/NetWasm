using System;
using System.Collections.Generic;
using System.IO;

namespace NetWasm.Hosting.Execution;

/// <summary>Validates descriptor structure; artifact freshness is checked separately before execution.</summary>
public sealed class ExecutionDescriptorValidator : IExecutionDescriptorValidator
{
    public void Validate(ExecutionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.SchemaVersion != 1)
        {
            throw new ArgumentException("Unsupported NetWasm execution descriptor schema.", nameof(descriptor));
        }

        ValidateDigest(descriptor.BuildFingerprint);
        ValidateDigest(descriptor.DeploymentManifestSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.HostingVersion);
        ValidatePath(descriptor.DeploymentManifestPath);
        ValidatePath(descriptor.HostExecutablePath);
        ValidatePath(descriptor.LauncherPath);
        if (descriptor.ToolPackages.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Execution requires restored tool package identities.", nameof(descriptor));
        }

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in descriptor.ToolPackages)
        {
            ArgumentNullException.ThrowIfNull(package);
            ArgumentException.ThrowIfNullOrWhiteSpace(package.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(package.Version);
            if (!identities.Add(package.Id))
            {
                throw new ArgumentException("Execution tool package identities must be unique.", nameof(descriptor));
            }

            ValidatePath(package.RootPath);
            ValidateDigest(package.Sha256);
        }
    }

    private static void ValidateDigest(string digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 64 || !System.Linq.Enumerable.All(digest, char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("Execution identities require lowercase SHA-256 digests.", nameof(digest));
        }
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Execution paths must be fully qualified local paths without NUL.", nameof(path));
        }
    }
}
