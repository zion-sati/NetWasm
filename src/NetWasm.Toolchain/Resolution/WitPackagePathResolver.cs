using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetWasm.Toolchain.Manifest;

namespace NetWasm.Toolchain.Resolution;

public sealed class WitPackagePathResolver(
    IPinnedPlatformAssetPathResolver assets) : IWitPackagePathResolver
{
    private readonly IPinnedPlatformAssetPathResolver _assets = assets ??
        throw new ArgumentNullException(nameof(assets));

    public WitPackagePaths Resolve()
    {
        var resolved = ToolchainPlatformAssetIds.WitPackageClosure
            .Select(assetId => RequireAsset(_assets.Resolve(assetId), assetId))
            .ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        var command = resolved[ToolchainPlatformAssetIds.WitCommandPackage];
        foreach (var asset in resolved.Values)
        {
            if (!string.Equals(command.PackageId, asset.PackageId, StringComparison.Ordinal)
                || !string.Equals(command.PackageVersion, asset.PackageVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Compiled WIT assets must belong to one Toolchain package identity.");
            }
        }

        return new(
            command.PackageId,
            command.PackageVersion,
            command.AbsolutePath,
            resolved[ToolchainPlatformAssetIds.WitAsyncCommandPackage].AbsolutePath,
            resolved[ToolchainPlatformAssetIds.WitCompilerPackage].AbsolutePath);
    }

    private static ResolvedPlatformAsset RequireAsset(
        ResolvedPlatformAsset? asset,
        string assetId)
    {
        if (asset is null
            || !string.Equals(asset.Id, assetId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(asset.PackageId)
            || string.IsNullOrWhiteSpace(asset.PackageVersion)
            || string.IsNullOrWhiteSpace(asset.Version)
            || !Path.IsPathFullyQualified(asset.AbsolutePath))
        {
            throw new InvalidOperationException(
                "The platform asset resolver returned an invalid compiled WIT product.");
        }

        return asset;
    }
}
