using System.Collections.Immutable;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Toolchain.Tests;

public sealed class WitPackagePathResolverTests
{
    private static readonly string PackageRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "netwasm-toolchain-wit"));

    [Fact]
    public void ResolveVerifiesAndReturnsEveryCompiledWitProduct()
    {
        var assets = new RecordingAssetResolver();
        var resolver = Assert.IsAssignableFrom<IWitPackagePathResolver>(
            new WitPackagePathResolver(assets));

        var result = resolver.Resolve();

        Assert.Equal(
            ToolchainPlatformAssetIds.WitPackageClosure.ToArray(),
            assets.Requests.ToArray());
        Assert.Equal("NetWasm.Toolchain", result.PackageId);
        Assert.Equal("0.2.0-preview.29", result.PackageVersion);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.WitCommandPackage),
            result.CommandPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.WitAsyncCommandPackage),
            result.AsyncCommandPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.WitCompilerPackage),
            result.CompilerPath);
    }

    [Fact]
    public void ResolveRejectsInvalidProductsAndSplitPackageIdentity()
    {
        Assert.Throws<InvalidOperationException>(() => new WitPackagePathResolver(
            new RecordingAssetResolver(
                ToolchainPlatformAssetIds.WitCommandPackage,
                CreateAsset(ToolchainPlatformAssetIds.WitCommandPackage) with
                {
                    AbsolutePath = "relative.wasm",
                })).Resolve());

        Assert.Throws<InvalidOperationException>(() => new WitPackagePathResolver(
            new RecordingAssetResolver(
                ToolchainPlatformAssetIds.WitCompilerPackage,
                CreateAsset(ToolchainPlatformAssetIds.WitCompilerPackage) with
                {
                    PackageVersion = "0.2.0-preview.28",
                })).Resolve());
    }

    [Fact]
    public void ConstructorRejectsMissingAssetResolver() =>
        Assert.Throws<ArgumentNullException>(() => new WitPackagePathResolver(null!));

    private static ResolvedPlatformAsset CreateAsset(string id) => new(
        "NetWasm.Toolchain",
        "0.2.0-preview.29",
        id,
        id switch
        {
            ToolchainPlatformAssetIds.WitCommandPackage => "0.2.11",
            ToolchainPlatformAssetIds.WitPackageManifest => "1",
            _ => "1.0.0",
        },
        AssetPath(id));

    private static string AssetPath(string id) => Path.Combine(
        PackageRoot,
        id switch
        {
            ToolchainPlatformAssetIds.WitPackageManifest =>
                "tools/wit-packages/wit-package-manifest.json",
            ToolchainPlatformAssetIds.WitCommandPackage =>
                "tools/wit-packages/command.wit.wasm",
            ToolchainPlatformAssetIds.WitAsyncCommandPackage =>
                "tools/wit-packages/async-command.wit.wasm",
            ToolchainPlatformAssetIds.WitCompilerPackage =>
                "tools/wit-packages/compiler.wit.wasm",
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
