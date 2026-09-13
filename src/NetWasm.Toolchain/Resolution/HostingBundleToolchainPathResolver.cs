using System;
using System.Collections.Generic;
using System.IO;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed class HostingBundleToolchainPathResolver :
    IHostingBundleToolchainPathResolver
{
    private readonly string _packageRoot;
    private readonly IPinnedPlatformAssetPathResolver _assets;
    private readonly IHostingBundleClosureIntegrityVerifier _closureIntegrityVerifier;

    public HostingBundleToolchainPathResolver(
        string packageRoot,
        IPinnedPlatformAssetPathResolver assets,
        IHostingBundleClosureIntegrityVerifier closureIntegrityVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        if (!Path.IsPathFullyQualified(packageRoot))
        {
            throw new ArgumentException(
                "The Toolchain package root must be an absolute path.",
                nameof(packageRoot));
        }

        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(closureIntegrityVerifier);
        _packageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        _assets = assets;
        _closureIntegrityVerifier = closureIntegrityVerifier;
    }

    public HostingBundleToolchainPaths Resolve(
        ResolvedHostExecutable nodeExecutable,
        ValidatedHostToolCompatibility nodeCompatibility)
    {
        ArgumentNullException.ThrowIfNull(nodeExecutable);
        ArgumentNullException.ThrowIfNull(nodeCompatibility);
        if (!string.Equals(nodeExecutable.ToolId, HostToolIds.Node, StringComparison.Ordinal)
            || !string.Equals(nodeCompatibility.ToolId, HostToolIds.Node,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Hosting bundle resolution requires resolved and validated Node products.");
        }

        if (!Path.IsPathFullyQualified(nodeExecutable.AbsolutePath))
        {
            throw new ArgumentException(
                "The validated Node executable path must be absolute.",
                nameof(nodeExecutable));
        }

        ArgumentNullException.ThrowIfNull(nodeCompatibility.Version);
        var resolved = new Dictionary<string, ResolvedPlatformAsset>(StringComparer.Ordinal);
        string? packageId = null;
        string? packageVersion = null;
        foreach (var assetId in ToolchainPlatformAssetIds.HostingBundleClosure)
        {
            var asset = _assets.Resolve(assetId) ??
                throw new InvalidOperationException(
                    "The platform asset resolver returned no asset.");
            RequireAsset(asset, assetId);
            packageId ??= asset.PackageId;
            packageVersion ??= asset.PackageVersion;
            if (!string.Equals(packageId, asset.PackageId, StringComparison.Ordinal)
                || !string.Equals(packageVersion, asset.PackageVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Hosting bundle assets must belong to one Toolchain package identity.");
            }
            resolved.Add(assetId, asset);
        }

        var package = resolved[ToolchainPlatformAssetIds.RolldownPackage];
        var entryPoint = resolved[ToolchainPlatformAssetIds.RolldownEntryPoint];
        if (!string.Equals(package.Version, entryPoint.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Rolldown package and entry point must use one pinned version.");
        }

        var integrity = resolved[ToolchainPlatformAssetIds.HostingBundleClosureIntegrity];
        _closureIntegrityVerifier.Verify(
            _packageRoot,
            Path.GetRelativePath(_packageRoot, integrity.AbsolutePath));

        return new(
            packageId!,
            packageVersion!,
            package.Version,
            nodeExecutable.AbsolutePath,
            nodeCompatibility.Version,
            resolved[ToolchainPlatformAssetIds.HostingBundleCommand].AbsolutePath,
            entryPoint.AbsolutePath,
            resolved[ToolchainPlatformAssetIds.HostingBundlePackageLock].AbsolutePath,
            integrity.AbsolutePath,
            resolved[ToolchainPlatformAssetIds.HostingBundleNotices].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.HostingBundleClosurePolicy].AbsolutePath);
    }

    private static void RequireAsset(ResolvedPlatformAsset asset, string assetId)
    {
        if (!string.Equals(asset.Id, assetId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(asset.PackageId)
            || string.IsNullOrWhiteSpace(asset.PackageVersion)
            || string.IsNullOrWhiteSpace(asset.Version)
            || !Path.IsPathFullyQualified(asset.AbsolutePath))
        {
            throw new InvalidOperationException(
                "The platform asset resolver returned an invalid Hosting bundle asset product.");
        }
    }
}
