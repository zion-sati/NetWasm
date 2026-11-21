// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Integration;

// Math and MathF call the C library's libm. The pack links libc_optz.a, Emscripten's size-optimized C library,
// before libc.a for its memory functions; its own log, log2 and pow are older, less accurate versions, so the pack
// leaves them out and those calls reach libc.a's, the libm Mono's WebAssembly runtime runs.
public sealed class SystemLibraryMathIntegrationTests
{
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    [Trait("Category", "NativeInteropIntegration")]
    public void TheSizeOptimizedCLibraryLeavesMathToLibc(string targetName)
    {
        var assetRoot = Path.Combine(RepositoryRoot(), "src", "NetWasm.Runtime.Pack", "runtime");
        var manifest = new RuntimePackManifestReader(new RuntimePackManifestParser())
            .Read(Path.Combine(assetRoot, "runtime-pack.json"));
        var target = new RuntimePackTargetSelector(new RuntimeOptimizationPolicyResolver()).Select(manifest, targetName, null);

        var optimized = Symbols(assetRoot, target, "libc_optz.a");
        var library = Symbols(assetRoot, target, "libc.a");

        Assert.Contains("memcpy", optimized);
        Assert.DoesNotContain("pow", optimized);
        Assert.DoesNotContain("log", optimized);
        Assert.DoesNotContain("log2", optimized);
        Assert.Contains("pow", library);
        Assert.Contains("log", library);
        Assert.Contains("log2", library);
    }

    private static ImmutableHashSet<string> Symbols(string assetRoot, RuntimePackTarget target, string name)
    {
        var asset = target.SystemLibraries.Assets[target.SystemLibraries.Names.IndexOf(name)];
        return RuntimeArchiveSymbolTable.Parse(File.ReadAllBytes(Path.Combine(assetRoot, asset.Path)));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "NetWasm.Runtime.Pack")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("The NetWasm repository root was not found.");
    }
}
