// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class HostToolsPackageResolver : IHostToolsPackageResolver
{
    // Node, the linker, Binaryen, nwc (the NetWasm compiler) and the native wasm-tools.
    private static readonly ImmutableArray<string> Roles =
        ["node", "nwc", "wasm-ld", "wasm-opt", "wasm-tools"];

    private static readonly ImmutableArray<string> WasmToolsNotices =
    [
        "licenses/wasm-tools-LICENSE-APACHE",
        "licenses/wasm-tools-LICENSE-Apache-2.0_WITH_LLVM-exception",
        "licenses/wasm-tools-LICENSE-MIT",
    ];

    private readonly IHostToolsPackageFileReader _files;
    private readonly IHostToolsPackageManifestReader _manifestReader;

    public HostToolsPackageResolver(
        IHostToolsPackageFileReader files,
        IHostToolsPackageManifestReader manifestReader)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
    }

    public HostToolsPackagePaths Resolve(HostToolsPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.HostRid);
        if (!Path.IsPathFullyQualified(request.PackageRoot))
        {
            throw new ArgumentException("The host-tools package root must be absolute.", nameof(request));
        }

        var expectedId = $"NetWasm.HostTools.{request.HostRid}";
        if (!string.Equals(request.PackageId, expectedId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected host-tools package {expectedId}.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.PackageRoot));
        var manifestPath = ResolvePath(root, "tools/host-tools-manifest.json");
        var manifestFile = Read(manifestPath);
        if (manifestFile.IsSymbolicLink)
        {
            throw new InvalidDataException("The host-tools manifest must be a regular file.");
        }

        var manifest = _manifestReader.Read(manifestFile.Bytes);
        if (manifest.PackageId != expectedId ||
            manifest.PackageVersion != request.PackageVersion ||
            manifest.HostRid != request.HostRid)
        {
            throw new InvalidDataException(
                $"Restored host-tools package must be {expectedId} {request.PackageVersion} for {request.HostRid}; restore again on this host.");
        }
        if (!Version.TryParse(manifest.NodeVersion, out _) ||
            !Version.TryParse(manifest.WasmLdVersion, out _) ||
            !int.TryParse(manifest.BinaryenVersion, out var binaryenVersion) || binaryenVersion <= 0)
        {
            throw new InvalidDataException("Host-tools package declares invalid tool versions.");
        }

        var suffix = request.HostRid.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : string.Empty;
        if (!Version.TryParse(manifest.WasmToolsVersion, out _))
        {
            throw new InvalidDataException("Host-tools package declares an invalid wasm-tools version.");
        }
        if (manifest.Roles.Count != Roles.Length ||
            Roles.Any(role => !manifest.Roles.TryGetValue(role, out var path) ||
                path != $"tools/bin/{role}{suffix}"))
        {
            throw new InvalidDataException("Host-tools package role map is incomplete or unexpected.");
        }

        var expectedPaths = manifest.Roles.Values.ToHashSet(StringComparer.Ordinal);
        expectedPaths.UnionWith(["LICENSE.txt", "README.md", "licenses/Node-LICENSE",
            "licenses/LLVM-LICENSE.txt", "licenses/LLD-LICENSE.txt",
            "licenses/LLVM-BLAKE3-LICENSE", "licenses/LLVM-ThirdParty-NOTICES.txt",
            "licenses/Binaryen-LICENSE", "licenses/NetWasm-LICENSE.txt", "licenses/nwc-THIRD-PARTY-NOTICES.txt"]);
        expectedPaths.UnionWith(WasmToolsNotices);
        if (request.HostRid.StartsWith("linux-", StringComparison.Ordinal))
        {
            expectedPaths.UnionWith(["tools/bin/libatomic.so.1",
                "licenses/GCC-Libatomic-COPYRIGHT", "licenses/GPL-3.0.txt",
                "licenses/GCC-Libatomic-SOURCE.txt"]);
        }
        if (manifest.Files.Length != expectedPaths.Count ||
            manifest.Files.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal).Count != expectedPaths.Count ||
            manifest.Files.Any(file => !expectedPaths.Contains(file.RelativePath)))
        {
            throw new InvalidDataException("Host-tools package file inventory is incomplete or unexpected.");
        }

        var paths = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            var absolutePath = ResolvePath(root, file.RelativePath);
            var content = Read(absolutePath);
            if (content.IsSymbolicLink || file.Size != content.Bytes.LongLength ||
                file.Sha256.Length != 64 ||
                !string.Equals(
                    file.Sha256,
                    Convert.ToHexString(SHA256.HashData(content.Bytes)),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Host-tools package file is missing or corrupt: {file.RelativePath}");
            }
            if (manifest.Roles.ContainsValue(file.RelativePath) &&
                !content.IsExecutable && !request.HostRid.StartsWith("win-", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Host-tools package executable lacks permission: {file.RelativePath}");
            }
            foreach (var role in Roles)
            {
                if (manifest.Roles[role] == file.RelativePath)
                {
                    paths.Add(role, absolutePath);
                }
            }
        }

        return new(expectedId, request.PackageVersion, request.HostRid,
            manifest.NodeVersion, manifest.WasmLdVersion, manifest.BinaryenVersion,
            paths.ToImmutable())
        {
            WasmToolsVersion = manifest.WasmToolsVersion,
        };
    }

    private HostToolsPackageFileContents Read(string path)
    {
        try
        {
            return _files.Read(path) ?? throw new InvalidDataException("Host-tools file reader returned no file.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Host-tools package file is unavailable: {path}", exception);
        }
    }

    private static string ResolvePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') ||
            relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"Unsafe host-tools package path: {relative}");
        }

        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException($"Host-tools package path escapes its root: {relative}");
        }
        return full;
    }
}
