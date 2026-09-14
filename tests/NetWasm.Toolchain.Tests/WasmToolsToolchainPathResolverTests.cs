using System.Collections.Immutable;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Toolchain.Tests;

public sealed class WasmToolsToolchainPathResolverTests
{
    private static readonly string PackageRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "netwasm-toolchain-wasm-tools"));
    private static readonly string NodePath = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "node"));

    [Fact]
    public void ResolveVerifiesTheExactDistributionAndReturnsNodeCommandPaths()
    {
        var assets = new RecordingAssetResolver();
        var resolver = Assert.IsAssignableFrom<IWasmToolsToolchainPathResolver>(
            new WasmToolsToolchainPathResolver(assets));

        var result = resolver.Resolve(Executable(), Compatibility());

        Assert.Equal(
            ToolchainPlatformAssetIds.WasmToolsClosure.ToArray(),
            assets.Requests.ToArray());
        Assert.Equal("NetWasm.Toolchain", result.PackageId);
        Assert.Equal("0.1.0-preview.29", result.PackageVersion);
        Assert.Equal("1.256.0", result.WasmToolsVersion);
        Assert.Equal(NodePath, result.NodePath);
        Assert.Equal(new Version(26, 7), result.NodeVersion);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.WasmToolsCommand), result.CommandPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.WasmToolsModule), result.ModulePath);
    }

    [Fact]
    public void ResolveRejectsInvalidNodeProductsBeforeReadingAssets()
    {
        var assets = new RecordingAssetResolver();
        var resolver = new WasmToolsToolchainPathResolver(assets);

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!, Compatibility()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(Executable(), null!));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            Executable() with { ToolId = HostToolIds.WasmLd }, Compatibility()));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            Executable(), Compatibility() with { ToolId = HostToolIds.WasmLd }));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            Executable() with { AbsolutePath = "node" }, Compatibility()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            Executable(), Compatibility() with { Version = null! }));
        Assert.Empty(assets.Requests);
    }

    [Fact]
    public void ResolveRejectsSplitPackageIdentityAndDistributionVersion()
    {
        var splitIdentity = CreateAsset(ToolchainPlatformAssetIds.WasmToolsModule) with
        {
            PackageVersion = "0.1.0-preview.28",
        };
        Assert.Throws<InvalidOperationException>(() => new WasmToolsToolchainPathResolver(
            new RecordingAssetResolver(
                ToolchainPlatformAssetIds.WasmToolsModule,
                splitIdentity)).Resolve(Executable(), Compatibility()));

        var splitVersion = CreateAsset(ToolchainPlatformAssetIds.WasmToolsMitLicense) with
        {
            Version = "1.255.0",
        };
        Assert.Throws<InvalidOperationException>(() => new WasmToolsToolchainPathResolver(
            new RecordingAssetResolver(
                ToolchainPlatformAssetIds.WasmToolsMitLicense,
                splitVersion)).Resolve(Executable(), Compatibility()));
    }

    [Fact]
    public void ConstructorRejectsMissingAssetResolver() =>
        Assert.Throws<ArgumentNullException>(() =>
            new WasmToolsToolchainPathResolver(null!));

    private static ResolvedHostExecutable Executable() => new(
        HostToolIds.Node,
        NodePath,
        HostExecutableResolutionSource.Path);

    private static ValidatedHostToolCompatibility Compatibility() => new(
        HostToolIds.Node,
        new Version(26, 7),
        []);

    private static ResolvedPlatformAsset CreateAsset(string id) => new(
        "NetWasm.Toolchain",
        "0.1.0-preview.29",
        id,
        string.Equals(id, ToolchainPlatformAssetIds.WasmToolsCommand,
            StringComparison.Ordinal) ? "1" : "1.256.0",
        AssetPath(id));

    private static string AssetPath(string id) => Path.Combine(
        PackageRoot,
        id switch
        {
            ToolchainPlatformAssetIds.WasmToolsCommand =>
                "tools/wasm-tools/run-wasm-tools.mjs",
            ToolchainPlatformAssetIds.WasmToolsModule =>
                "tools/wasm-tools/wasm-tools.wasm",
            ToolchainPlatformAssetIds.WasmToolsApacheLicense =>
                "tools/wasm-tools/LICENSE-APACHE",
            ToolchainPlatformAssetIds.WasmToolsApacheLlvmLicense =>
                "tools/wasm-tools/LICENSE-Apache-2.0_WITH_LLVM-exception",
            ToolchainPlatformAssetIds.WasmToolsMitLicense =>
                "tools/wasm-tools/LICENSE-MIT",
            ToolchainPlatformAssetIds.WasmToolsReadme =>
                "tools/wasm-tools/README.md",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
        });

    private sealed class RecordingAssetResolver(
        string? selectedId = null,
        ResolvedPlatformAsset? selectedResult = null) :
        IPinnedPlatformAssetPathResolver
    {
        private readonly List<string> _requests = [];

        public ImmutableArray<string> Requests => [.. _requests];

        public ResolvedPlatformAsset Resolve(string assetId)
        {
            _requests.Add(assetId);
            return string.Equals(assetId, selectedId, StringComparison.Ordinal)
                ? selectedResult!
                : CreateAsset(assetId);
        }
    }
}
