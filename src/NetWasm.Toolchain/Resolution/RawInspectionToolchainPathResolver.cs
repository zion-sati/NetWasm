using System;
using System.Collections.Generic;
using System.IO;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed class RawInspectionToolchainPathResolver(
    IPinnedPlatformAssetPathResolver assets) : IRawInspectionToolchainPathResolver
{
    private readonly IPinnedPlatformAssetPathResolver _assets = assets ??
        throw new ArgumentNullException(nameof(assets));

    public RawInspectionToolchainPaths Resolve(
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
                "Raw inspection requires resolved and validated Node products.");
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
        foreach (var assetId in ToolchainPlatformAssetIds.RawInspectionClosure)
        {
            var asset = _assets.Resolve(assetId) ??
                throw new InvalidOperationException(
                    "The platform asset resolver returned no asset.");
            if (!string.Equals(asset.Id, assetId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(asset.PackageId)
                || string.IsNullOrWhiteSpace(asset.PackageVersion)
                || string.IsNullOrWhiteSpace(asset.Version)
                || !Path.IsPathFullyQualified(asset.AbsolutePath))
            {
                throw new InvalidOperationException(
                    "The platform asset resolver returned an invalid asset product.");
            }

            packageId ??= asset.PackageId;
            packageVersion ??= asset.PackageVersion;
            if (!string.Equals(packageId, asset.PackageId, StringComparison.Ordinal)
                || !string.Equals(packageVersion, asset.PackageVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Raw inspection assets must belong to one Toolchain package identity.");
            }

            resolved.Add(assetId, asset);
        }

        return new(
            packageId!,
            packageVersion!,
            nodeExecutable.AbsolutePath,
            nodeCompatibility.Version,
            resolved[ToolchainPlatformAssetIds.RawInspectionCommand].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.BinaryenModule].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.BinaryenWasmOpt].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.BinaryenWasmMerge].AbsolutePath);
    }
}
