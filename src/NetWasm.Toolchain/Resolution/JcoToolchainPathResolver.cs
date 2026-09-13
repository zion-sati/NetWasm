using System;
using System.Collections.Generic;
using System.IO;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed class JcoToolchainPathResolver : IJcoToolchainPathResolver
{
    private readonly string _packageRoot;
    private readonly IPinnedPlatformAssetPathResolver _assets;
    private readonly IJcoClosureIntegrityVerifier _closureIntegrityVerifier;

    public JcoToolchainPathResolver(
        string packageRoot,
        IPinnedPlatformAssetPathResolver assets,
        IJcoClosureIntegrityVerifier closureIntegrityVerifier)
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

    public JcoToolchainPaths Resolve(
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
                "Jco resolution requires resolved and validated Node products.");
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
        foreach (var assetId in ToolchainPlatformAssetIds.JcoClosure)
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
                    "Jco assets must belong to one Toolchain package identity.");
            }

            resolved.Add(assetId, asset);
        }

        var jcoPackage = resolved[ToolchainPlatformAssetIds.JcoPackage];
        var jcoEntryPoint = resolved[ToolchainPlatformAssetIds.JcoEntryPoint];
        if (!string.Equals(jcoPackage.Version, jcoEntryPoint.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The jco package and entry point must use one pinned jco version.");
        }

        var integrity = resolved[ToolchainPlatformAssetIds.JcoClosureIntegrity];
        var integrityRelativePath = Path.GetRelativePath(
            _packageRoot,
            integrity.AbsolutePath);
        _closureIntegrityVerifier.Verify(_packageRoot, integrityRelativePath);

        return new(
            packageId!,
            packageVersion!,
            resolved[ToolchainPlatformAssetIds.JcoPackage].Version,
            resolved[ToolchainPlatformAssetIds.Preview2ShimPackage].Version,
            nodeExecutable.AbsolutePath,
            nodeCompatibility.Version,
            resolved[ToolchainPlatformAssetIds.JcoEntryPoint].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.JcoPackageLock].AbsolutePath,
            integrity.AbsolutePath,
            resolved[ToolchainPlatformAssetIds.JcoNotices].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.JcoClosurePolicy].AbsolutePath);
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
                "The platform asset resolver returned an invalid jco asset product.");
        }
    }
}
