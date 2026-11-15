// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.IO;
using NetWasm.Toolchain.Manifest;

namespace NetWasm.Toolchain.Resolution;

/// <summary>The compiled IANA zones a publish reads to build a time zone sidecar.</summary>
public sealed record TimeZoneSourcePaths(
    string PackageId,
    string PackageVersion,
    string SourceRoot,
    string DataVersion);

public interface ITimeZoneSourcePathResolver
{
    TimeZoneSourcePaths Resolve();
}

/// <summary>
/// Resolves the pinned zone source. The manifest digest binds the integrity file,
/// which binds every zone file, and the source's IANA version must match the pin.
/// </summary>
public sealed class TimeZoneSourcePathResolver : ITimeZoneSourcePathResolver
{
    private readonly string _packageRoot;
    private readonly IPinnedPlatformAssetPathResolver _assets;

    public TimeZoneSourcePathResolver(string packageRoot, IPinnedPlatformAssetPathResolver assets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        if (!Path.IsPathFullyQualified(packageRoot))
        {
            throw new ArgumentException(
                "The Toolchain package root must be an absolute path.",
                nameof(packageRoot));
        }

        _packageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    public TimeZoneSourcePaths Resolve()
    {
        var integrity = _assets.Resolve(ToolchainPlatformAssetIds.TimeZoneClosureIntegrity);
        JavaScriptClosureIntegrityVerifier.Verify(
            _packageRoot,
            Path.GetRelativePath(_packageRoot, integrity.AbsolutePath),
            allowBundleCommand: false,
            allowJcoPatchProvenance: false);
        var sourceRoot = Path.Combine(Path.GetDirectoryName(integrity.AbsolutePath)!, "zoneinfo");
        var versionPath = Path.Combine(sourceRoot, "version");
        if (!File.Exists(versionPath))
        {
            throw new ToolchainAssetMissingException(versionPath);
        }

        var version = File.ReadAllText(versionPath).Trim();
        if (!string.Equals(version, integrity.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The Toolchain time zone source is IANA {version}, but the package pins {integrity.Version}.");
        }

        return new(integrity.PackageId, integrity.PackageVersion, sourceRoot, version);
    }
}
