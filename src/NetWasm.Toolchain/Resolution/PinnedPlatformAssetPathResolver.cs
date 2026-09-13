using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Toolchain.Manifest;

namespace NetWasm.Toolchain.Resolution;

public sealed class PinnedPlatformAssetPathResolver : IPinnedPlatformAssetPathResolver
{
    private const string SupportedSchemaVersion = "1";

    private readonly string _packageRoot;
    private readonly string _packageId;
    private readonly string _packageVersion;
    private readonly ImmutableDictionary<string, PinnedPlatformAssetDescriptor> _assets;
    private readonly IFilePresenceChecker _presenceChecker;
    private readonly IArtifactDigestVerifier _digestVerifier;

    public PinnedPlatformAssetPathResolver(
        string packageRoot,
        PinnedToolchainManifest manifest,
        IFilePresenceChecker presenceChecker,
        IArtifactDigestVerifier digestVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        if (!Path.IsPathFullyQualified(packageRoot))
        {
            throw new ArgumentException(
                "The Toolchain package root must be an absolute path.",
                nameof(packageRoot));
        }

        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(presenceChecker);
        ArgumentNullException.ThrowIfNull(digestVerifier);
        RequireManifestIdentity(manifest);

        _packageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        _packageId = manifest.PackageId;
        _packageVersion = manifest.PackageVersion;
        _presenceChecker = presenceChecker;
        _digestVerifier = digestVerifier;
        _assets = CreateAssetMap(manifest.Assets, _packageRoot);
    }

    public ResolvedPlatformAsset Resolve(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (!_assets.TryGetValue(assetId, out var descriptor))
        {
            throw new UnsupportedPlatformAssetException(assetId);
        }

        var absolutePath = ResolveContainedPath(_packageRoot, descriptor.RelativePath);
        if (!_presenceChecker.Exists(absolutePath))
        {
            throw new ToolchainAssetMissingException(absolutePath);
        }

        RequirePhysicallyContainedFile(_packageRoot, absolutePath);
        _digestVerifier.Verify(absolutePath, descriptor.Sha256);
        return new(
            _packageId,
            _packageVersion,
            descriptor.Id,
            descriptor.Version,
            absolutePath);
    }

    private static void RequireManifestIdentity(PinnedToolchainManifest manifest)
    {
        if (!string.Equals(manifest.SchemaVersion, SupportedSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Toolchain asset manifest schema is unsupported.",
                nameof(manifest));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.PackageVersion);
        if (manifest.Assets.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "The Toolchain asset manifest must contain platform assets.",
                nameof(manifest));
        }
    }

    private static ImmutableDictionary<string, PinnedPlatformAssetDescriptor> CreateAssetMap(
        ImmutableArray<PinnedPlatformAssetDescriptor> descriptors,
        string packageRoot)
    {
        var assets = ImmutableDictionary.CreateBuilder<string, PinnedPlatformAssetDescriptor>(
            StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Version);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RelativePath);
            RequireSha256(descriptor.Sha256);
            _ = ResolveContainedPath(packageRoot, descriptor.RelativePath);
            if (assets.ContainsKey(descriptor.Id))
            {
                throw new ArgumentException(
                    $"Duplicate platform asset ID '{descriptor.Id}'.",
                    nameof(descriptors));
            }

            assets.Add(descriptor.Id, descriptor);
        }

        return assets.ToImmutable();
    }

    private static string ResolveContainedPath(string packageRoot, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new ArgumentException(
                "Toolchain asset paths must be relative to the package root.",
                nameof(relativePath));
        }

        var absolutePath = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        var relativeToRoot = Path.GetRelativePath(packageRoot, absolutePath);
        if (string.Equals(relativeToRoot, ".", StringComparison.Ordinal)
            || string.Equals(relativeToRoot, "..", StringComparison.Ordinal)
            || relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativeToRoot))
        {
            throw new ArgumentException(
                "Toolchain asset paths must remain inside the package root.",
                nameof(relativePath));
        }

        return absolutePath;
    }

    private static void RequireSha256(string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (sha256.Length != 64)
        {
            throw new ArgumentException(
                "Toolchain asset SHA-256 values must contain 64 hexadecimal characters.",
                nameof(sha256));
        }

        if (sha256.All(character => character == '0'))
        {
            throw new ArgumentException(
                "Toolchain asset SHA-256 values must identify a non-empty digest.",
                nameof(sha256));
        }

        foreach (var character in sha256)
        {
            if (!Uri.IsHexDigit(character))
            {
                throw new ArgumentException(
                    "Toolchain asset SHA-256 values must contain 64 hexadecimal characters.",
                    nameof(sha256));
            }
        }
    }

    private static void RequirePhysicallyContainedFile(string packageRoot, string assetPath)
    {
        // Test doubles may model a virtual file system, so only inspect the
        // physical path when the path is present on the host. In production
        // this prevents a package directory or asset symlink from redirecting
        // digest verification outside the extracted package.
        if (!File.Exists(assetPath))
        {
            return;
        }

        var physicalRoot = ResolvePhysicalPath(packageRoot);
        var physicalAsset = ResolvePhysicalPath(assetPath);
        var relative = Path.GetRelativePath(physicalRoot, physicalAsset);
        if (string.Equals(relative, ".", StringComparison.Ordinal)
            || string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative))
        {
            throw new InvalidDataException(
                "The Toolchain asset path resolves outside the package root.");
        }
    }

    private static string ResolvePhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        var remainder = fullPath[root.Length..];
        var parts = remainder.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            FileSystemInfo link = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            var target = link.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                current = target.FullName;
            }
        }

        return Path.GetFullPath(current);
    }
}
