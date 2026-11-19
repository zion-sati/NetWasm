// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Runtime.Pack.MsBuild;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.MsBuild;

public sealed class PackageNativeArchiveSelectionTaskTests
{
    // The restored packages are read from the assets file's package folders; the runtime pack's manifest names the
    // Emscripten version.
    [Fact]
    public void ReturnsTheMatchingBuildOfEachPackagesLibrariesAsNativeLibraries()
    {
        using var directory = new TemporaryDirectory();
        var skia = Archive(directory, "skiasharp.nativeassets.webassembly/3.119.4", "libSkiaSharp", "3.1.56/st,simd");
        Archive(directory, "skiasharp.nativeassets.webassembly/3.119.4", "libSkiaSharp", "3.1.56/mt");
        Archive(directory, "skiasharp.nativeassets.webassembly/3.119.4", "libSkiaSharp", "3.1.34/st,simd");
        var task = Create(directory);

        Assert.True(task.Execute());

        var item = Assert.Single(task.NativeLibraries);
        Assert.Equal(skia, item.ItemSpec);
        Assert.Equal("libSkiaSharp", item.GetMetadata("NetWasmLibraryName"));
        Assert.Equal("wasm32", item.GetMetadata("WasmTarget"));
        Assert.Equal("SkiaSharp.NativeAssets.WebAssembly", item.GetMetadata("NetWasmPackageId"));
        Assert.Equal("3.1.56", item.GetMetadata("NetWasmEmscriptenVersion"));
    }

    // A library the project names for this target keeps the project's archive.
    [Fact]
    public void LeavesALibraryTheProjectNamesToTheProject()
    {
        using var directory = new TemporaryDirectory();
        Archive(directory, "skiasharp.nativeassets.webassembly/3.119.4", "libSkiaSharp", "3.1.56/st,simd");
        var named = new TaskItem("/project/libSkiaSharp.a");
        named.SetMetadata("NetWasmLibraryName", "libSkiaSharp");
        named.SetMetadata("WasmTarget", "wasm32");
        var otherTarget = new TaskItem("/project/libSkiaSharp64.a");
        otherTarget.SetMetadata("NetWasmLibraryName", "libSkiaSharp");
        otherTarget.SetMetadata("WasmTarget", "wasm64");

        var task = Create(directory);
        task.ExistingNativeLibraries = [named];
        Assert.True(task.Execute());
        Assert.Empty(task.NativeLibraries);

        task = Create(directory);
        task.ExistingNativeLibraries = [otherTarget];
        Assert.True(task.Execute());
        Assert.Single(task.NativeLibraries);
    }

    [Fact]
    public void AManifestWithoutAnEmscriptenVersionFailsTheBuild()
    {
        using var directory = new TemporaryDirectory();
        var task = Create(directory);
        File.WriteAllText(task.RuntimePackManifestPath, "{}");
        var engine = (Engine)task.BuildEngine;

        Assert.False(task.Execute());

        Assert.Equal("NWPACK020", Assert.Single(engine.Errors).Code);
    }

    private static string Archive(TemporaryDirectory directory, string package, string library, string build)
    {
        var path = Path.Combine(directory.Path, "packages", package, "buildTransitive", "netstandard1.0", library + ".a",
            build, library + ".a");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "archive");
        return path;
    }

    private static PackageNativeArchiveSelectionTask Create(TemporaryDirectory directory)
    {
        var packages = Path.Combine(directory.Path, "packages") + Path.DirectorySeparatorChar;
        var assets = Path.Combine(directory.Path, "project.assets.json");
        File.WriteAllText(assets, JsonSerializer.Serialize(new
        {
            packageFolders = new Dictionary<string, object> { [packages] = new { } },
            libraries = new Dictionary<string, object>
            {
                ["SkiaSharp.NativeAssets.WebAssembly/3.119.4"] = new { type = "package", path = "skiasharp.nativeassets.webassembly/3.119.4" },
                ["App.Library/1.0.0"] = new { type = "project", path = "../library/library.csproj" },
                ["Missing/1.0.0"] = new { type = "package", path = "missing/1.0.0" },
            },
        }));
        var manifest = Path.Combine(directory.Path, "runtime-pack.json");
        File.WriteAllText(manifest, """{ "emscriptenVersion": "6.0.7" }""");
        return new()
        {
            BuildEngine = new Engine(),
            ProjectAssetsFile = assets,
            RuntimePackManifestPath = manifest,
            WasmTarget = "wasm32",
        };
    }

    private sealed class Engine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public int ColumnNumberOfTaskNode => 0;
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(string projectFileName, string[] targetNames,
            System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => false;

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }
    }
}
