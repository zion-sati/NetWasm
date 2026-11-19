// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Runtime.Pack.PackageNatives;

namespace NetWasm.Runtime.Pack.MsBuild;

// The static archives the restored packages ship per Emscripten version, as NativeLibrary items: the matching build of
// each library a package provides, unless the project names that library itself.
public sealed class PackageNativeArchiveSelectionTask : Task
{
    [Required]
    public string ProjectAssetsFile { get; set; } = string.Empty;

    [Required]
    public string RuntimePackManifestPath { get; set; } = string.Empty;

    [Required]
    public string WasmTarget { get; set; } = string.Empty;

    public ITaskItem[] ExistingNativeLibraries { get; set; } = [];

    [Output]
    public ITaskItem[] NativeLibraries { get; private set; } = [];

    public override bool Execute()
    {
        try
        {
            var emscripten = EmscriptenVersion(RuntimePackManifestPath);
            var named = ExistingNativeLibraries
                .Where(item => string.Equals(item.GetMetadata("WasmTarget"), WasmTarget, StringComparison.Ordinal))
                .Select(item => item.GetMetadata("NetWasmLibraryName"))
                .ToHashSet(StringComparer.Ordinal);
            NativeLibraries = [.. PackageNativeArchiveSelector.Select(PackageFiles(ProjectAssetsFile), emscripten)
                .Where(archive => !named.Contains(archive.LibraryName))
                .Select(archive =>
                {
                    var item = new TaskItem(archive.Path);
                    item.SetMetadata("NetWasmLibraryName", archive.LibraryName);
                    item.SetMetadata("WasmTarget", WasmTarget);
                    item.SetMetadata("NetWasmPackageId", archive.PackageId);
                    item.SetMetadata("NetWasmEmscriptenVersion", archive.EmscriptenVersion.ToString());
                    return (ITaskItem)item;
                })];
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            Log.LogError(null, "NWPACK020", null, null, 0, 0, 0, 0,
                "Selecting the packages' native archives failed: {0}", exception.Message);
            return false;
        }
    }

    // The runtime pack's Emscripten version, which built its system libraries.
    private static Version EmscriptenVersion(string manifestPath)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        return manifest.RootElement.TryGetProperty("emscriptenVersion", out var value) &&
            Version.TryParse(value.GetString(), out var version)
            ? version
            : throw new InvalidOperationException("the runtime pack manifest names no Emscripten version");
    }

    // Every archive under each restored package's build and buildTransitive folders and its browser-wasm native assets,
    // from the assets file's package folders.
    private static IEnumerable<PackageFile> PackageFiles(string assetsPath)
    {
        using var assets = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        var folders = assets.RootElement.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToArray();
        foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package" ||
                !library.Value.TryGetProperty("path", out var path) || path.GetString() is not { } relative)
            {
                continue;
            }
            var root = folders.Select(folder => Path.Combine(folder, relative)).FirstOrDefault(Directory.Exists);
            if (root is null)
            {
                continue;
            }
            var id = library.Name[..library.Name.IndexOf('/')];
            foreach (var top in new[] { "build", "buildTransitive", Path.Combine("runtimes", "browser-wasm", "nativeassets") })
            {
                var directory = Path.Combine(root, top);
                if (!Directory.Exists(directory))
                {
                    continue;
                }
                foreach (var file in Directory.EnumerateFiles(directory, "*.a", SearchOption.AllDirectories))
                {
                    yield return new(id, root, Path.GetRelativePath(root, file).Replace('\\', '/'));
                }
            }
        }
    }
}
