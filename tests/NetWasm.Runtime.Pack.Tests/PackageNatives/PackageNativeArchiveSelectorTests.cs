// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Runtime.Pack.PackageNatives;

namespace NetWasm.Runtime.Pack.Tests.PackageNatives;

public sealed class PackageNativeArchiveSelectorTests
{
    private static readonly Version Emscripten = new(6, 0, 7);

    // SkiaSharp 3.119's layout: per Emscripten version, per variant, newest first no newer than NetWasm's Emscripten,
    // single-threaded, SIMD preferred.
    [Fact]
    public void TakesTheNewestSingleThreadedBuildNoNewerThanTheRuntimesEmscripten()
    {
        var selected = PackageNativeArchiveSelector.Select(
            Files("SkiaSharp.NativeAssets.WebAssembly", "libSkiaSharp",
                "3.1.12/st", "3.1.12/st,simd", "3.1.34/st,simd", "3.1.56/mt", "3.1.56/mt,simd", "3.1.56/st",
                "3.1.56/st,simd", "6.0.8/st,simd", "2.0.23", "3.1.7"),
            Emscripten);

        var archive = Assert.Single(selected);
        Assert.Equal("libSkiaSharp", archive.LibraryName);
        Assert.Equal("SkiaSharp.NativeAssets.WebAssembly", archive.PackageId);
        Assert.Equal(new Version(3, 1, 56), archive.EmscriptenVersion);
        Assert.Equal("st,simd", archive.Variant);
        Assert.Equal("/packages/skiasharp/buildTransitive/netstandard1.0/libSkiaSharp.a/3.1.56/st,simd/libSkiaSharp.a",
            archive.Path);
    }

    [Fact]
    public void FallsBackToTheSingleThreadedBuildWithoutSimdThenToAnUnvariedBuild()
    {
        Assert.Equal("st", Assert.Single(PackageNativeArchiveSelector.Select(
            Files("P", "libZ", "3.1.56/st", "3.1.56/mt,simd"), Emscripten)).Variant);
        var unvaried = Assert.Single(PackageNativeArchiveSelector.Select(
            Files("P", "libZ", "3.1.7", "2.0.23", "3.1.12/mt"), Emscripten));
        Assert.Equal(new Version(3, 1, 7), unvaried.EmscriptenVersion);
        Assert.Equal("", unvaried.Variant);
    }

    // A version with only multithreaded builds is skipped for an older one NetWasm can link.
    [Fact]
    public void SkipsVersionsWithoutASingleThreadedBuild()
    {
        var archive = Assert.Single(PackageNativeArchiveSelector.Select(
            Files("P", "libZ", "3.1.56/mt", "3.1.56/mt,simd", "3.1.34/st"), Emscripten));

        Assert.Equal(new Version(3, 1, 34), archive.EmscriptenVersion);
    }

    [Fact]
    public void SelectsNothingWhenEveryBuildIsNewerOrMultithreaded()
    {
        Assert.Empty(PackageNativeArchiveSelector.Select(Files("P", "libZ", "6.0.8/st", "7.0.0", "3.1.56/mt"), Emscripten));
    }

    // Only <library>.a/<version>/[<variant>/]<library>.a under build or buildTransitive counts; other archives in a
    // package (a flat archive, a differently named sibling, lib/ assets) are left to the project.
    [Fact]
    public void IgnoresFilesOutsideTheLayout()
    {
        PackageFile[] files =
        [
            new("P", "/p", "buildTransitive/netstandard1.0/libZ.a"),
            new("P", "/p", "buildTransitive/netstandard1.0/libZ.a/3.1.56/st/libZ.wasm.a"),
            new("P", "/p", "lib/netstandard1.0/libZ.a/3.1.56/st/libZ.a"),
            new("P", "/p", "build/netstandard1.0/libZ.a/latest/st/libZ.a"),
            new("P", "/p", "build/netstandard1.0/libZ.a/3.1.56/st/extra/libZ.a"),
            new("P", "/p", "build/netstandard1.0/libZ.a/3.1.56/st,threads/libZ.a"),
        ];

        Assert.Empty(PackageNativeArchiveSelector.Select(files, Emscripten));
    }

    // Each package's libraries are chosen on their own and listed by library name.
    [Fact]
    public void ChoosesPerPackageAndLibrary()
    {
        var selected = PackageNativeArchiveSelector.Select(
        [
            .. Files("SkiaSharp.NativeAssets.WebAssembly", "libSkiaSharp", "3.1.56/st,simd"),
            .. Files("HarfBuzzSharp.NativeAssets.WebAssembly", "libHarfBuzzSharp", "3.1.34/st", "3.1.56/st,simd"),
            new PackageFile("P", "/p", "build/net8.0/libA.a/3.1.12/libA.a"),
        ], Emscripten);

        Assert.Equal(["libA", "libHarfBuzzSharp", "libSkiaSharp"], selected.Select(archive => archive.LibraryName));
        Assert.Equal("/p/build/net8.0/libA.a/3.1.12/libA.a", selected[0].Path);
    }

    // SQLitePCLRaw's layout, .NET's runtime native assets: one archive per framework, built with the Emscripten that
    // framework's browser-wasm SDK uses (net9.0 and net10.0 3.1.56, net11.0 6.0.2). The newest no newer than NetWasm's
    // Emscripten wins; a framework .NET has not released is skipped.
    [Fact]
    public void ReadsTheFrameworkOfDotNetsBrowserWasmNativeAssets()
    {
        static PackageFile Asset(string framework) =>
            new("SQLitePCLRaw.lib.e_sqlite3", "/p", $"runtimes/browser-wasm/nativeassets/{framework}/e_sqlite3.a");

        var archive = Assert.Single(PackageNativeArchiveSelector.Select(
            [Asset("net6.0"), Asset("net7.0"), Asset("net8.0"), Asset("net9.0")], Emscripten));
        Assert.Equal("e_sqlite3", archive.LibraryName);
        Assert.Equal(new Version(3, 1, 56), archive.EmscriptenVersion);
        Assert.Equal("", archive.Variant);
        Assert.Equal("/p/runtimes/browser-wasm/nativeassets/net9.0/e_sqlite3.a", archive.Path);

        Assert.Equal(new Version(6, 0, 2), Assert.Single(PackageNativeArchiveSelector.Select(
            [Asset("net8.0"), Asset("net11.0"), Asset("net12.0")], Emscripten)).EmscriptenVersion);
        Assert.Equal(new Version(3, 1, 34), Assert.Single(PackageNativeArchiveSelector.Select(
            [Asset("net8.0"), Asset("net11.0")], new Version(4, 0, 0))).EmscriptenVersion);
        Assert.Empty(PackageNativeArchiveSelector.Select(
        [
            Asset("net12.0"),
            Asset("netstandard2.0"),
            new("P", "/p", "runtimes/browser-wasm/nativeassets/net9.0/e_sqlite3.so"),
            new("P", "/p", "runtimes/linux-x64/nativeassets/net9.0/e_sqlite3.a"),
            new("P", "/p", "runtimes/browser-wasm/nativeassets/net9.0/mt/e_sqlite3.a"),
            new("P", "/p", "runtimes/browser-wasm/native/e_sqlite3.a"),
        ], Emscripten));
    }

    [Fact]
    public void RequiresFilesAndAVersion()
    {
        Assert.Throws<ArgumentNullException>(() => PackageNativeArchiveSelector.Select(null!, Emscripten));
        Assert.Throws<ArgumentNullException>(() => PackageNativeArchiveSelector.Select([], null!));
    }

    private static IEnumerable<PackageFile> Files(string package, string library, params string[] builds) =>
        builds.Select(build => new PackageFile(package, $"/packages/{package.Split('.')[0].ToLowerInvariant()}",
            $"buildTransitive/netstandard1.0/{library}.a/{build}/{library}.a"));
}
