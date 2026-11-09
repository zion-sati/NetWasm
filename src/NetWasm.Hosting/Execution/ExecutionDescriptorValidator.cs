using System;
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
