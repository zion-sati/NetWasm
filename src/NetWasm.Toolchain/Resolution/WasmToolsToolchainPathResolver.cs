using System;
using System.Collections.Generic;
using System.IO;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed class WasmToolsToolchainPathResolver(
    IPinnedPlatformAssetPathResolver assets) : IWasmToolsToolchainPathResolver
{
    private readonly IPinnedPlatformAssetPathResolver _assets = assets ??
        throw new ArgumentNullException(nameof(assets));

    public WasmToolsToolchainPaths Resolve(
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
                "wasm-tools resolution requires resolved and validated Node products.");
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
        foreach (var assetId in ToolchainPlatformAssetIds.WasmToolsClosure)
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
                    "wasm-tools assets must belong to one Toolchain package identity.");
            }
            resolved.Add(assetId, asset);
        }

        var module = resolved[ToolchainPlatformAssetIds.WasmToolsModule];
        foreach (var assetId in ToolchainPlatformAssetIds.WasmToolsDistribution)
        {
            if (!string.Equals(module.Version, resolved[assetId].Version,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The wasm-tools distribution assets must use one pinned version.");
            }
        }

        return new(
            packageId!,
            packageVersion!,
            module.Version,
            nodeExecutable.AbsolutePath,
            nodeCompatibility.Version,
            resolved[ToolchainPlatformAssetIds.WasmToolsCommand].AbsolutePath,
            module.AbsolutePath,
            resolved[ToolchainPlatformAssetIds.WasmToolsApacheLicense].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.WasmToolsApacheLlvmLicense].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.WasmToolsMitLicense].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.WasmToolsReadme].AbsolutePath);
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
                "The platform asset resolver returned an invalid wasm-tools asset product.");
        }
    }
}
