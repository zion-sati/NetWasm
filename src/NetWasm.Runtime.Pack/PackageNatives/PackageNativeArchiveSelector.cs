// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Runtime.Pack.PackageNatives;

// One file in a restored package, by its path from the package's root with '/' separators.
internal sealed record PackageFile(string PackageId, string PackageRoot, string RelativePath);

// A package's static archive for a library, built by one Emscripten version.
internal sealed record PackageNativeArchive(string PackageId, string LibraryName, string Path, Version EmscriptenVersion, string Variant);

// The build a package ships per Emscripten version, in either of the layouts packages use for .NET's WebAssembly SDKs:
// - SkiaSharp's and HarfBuzzSharp's: build[Transitive]/<framework>/<library>.a/<Emscripten version>/[<variant>/]<library>.a.
// - .NET's runtime native assets, as SQLitePCLRaw's: runtimes/browser-wasm/nativeassets/<framework>/<library>.a, built
//   with the Emscripten that framework's browser-wasm SDK uses.
// NetWasm links the newest build no newer than the Emscripten its runtime pack was built with, single-threaded (NetWasm
// has no threads), with SIMD where there is one; a build in no variant folder is a single-threaded one.
internal static class PackageNativeArchiveSelector
{
    private static readonly string[] VariantPreference = ["st,simd", "st", ""];

    // The Emscripten version each .NET release's browser-wasm SDK builds with (src/mono/browser/emscripten-version.txt,
    // src/mono/wasm/ before .NET 9, at each release's tag). A framework not listed is skipped.
    private static readonly Dictionary<int, Version> FrameworkEmscripten = new()
    {
        [6] = new(2, 0, 23),
        [7] = new(3, 1, 12),
        [8] = new(3, 1, 34),
        [9] = new(3, 1, 56),
        [10] = new(3, 1, 56),
        [11] = new(6, 0, 2),
    };

    public static ImmutableArray<PackageNativeArchive> Select(IEnumerable<PackageFile> files, Version emscriptenVersion)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(emscriptenVersion);
        return [.. files
            .Select(Parse)
            .OfType<PackageNativeArchive>()
            .Where(archive => archive.EmscriptenVersion <= emscriptenVersion && VariantPreference.Contains(archive.Variant))
            .GroupBy(archive => (archive.PackageId, archive.LibraryName))
            .Select(group => group
                .OrderByDescending(archive => archive.EmscriptenVersion)
                .ThenBy(archive => Array.IndexOf(VariantPreference, archive.Variant))
                .ThenBy(archive => archive.Path, StringComparer.Ordinal)
                .First())
            .OrderBy(archive => archive.LibraryName, StringComparer.Ordinal)
            .ThenBy(archive => archive.PackageId, StringComparer.OrdinalIgnoreCase)];
    }

    private static PackageNativeArchive? Parse(PackageFile file)
    {
        var parts = file.RelativePath.Split('/');
        // runtimes, browser-wasm, nativeassets, framework, <library>.a
        if (parts is ["runtimes", "browser-wasm", "nativeassets", var framework, var name] &&
            name.EndsWith(".a", StringComparison.Ordinal) && name.Length > 2 &&
            framework.StartsWith("net", StringComparison.Ordinal) &&
            Version.TryParse(framework[3..], out var frameworkVersion) &&
            FrameworkEmscripten.TryGetValue(frameworkVersion.Major, out var emscripten))
        {
            return new(file.PackageId, name[..^2], System.IO.Path.Combine(file.PackageRoot, file.RelativePath), emscripten,
                string.Empty);
        }
        // build[Transitive], framework, <library>.a, version, [variant], <library>.a
        if (parts.Length is not (5 or 6) || parts[0] is not ("build" or "buildTransitive"))
        {
            return null;
        }
        var directory = parts[2];
        if (!directory.EndsWith(".a", StringComparison.Ordinal) || parts[^1] != directory ||
            !Version.TryParse(parts[3], out var version))
        {
            return null;
        }
        return new(file.PackageId, directory[..^2], System.IO.Path.Combine(file.PackageRoot, file.RelativePath), version,
            parts.Length == 6 ? parts[4] : string.Empty);
    }
}
