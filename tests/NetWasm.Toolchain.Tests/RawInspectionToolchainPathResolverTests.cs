using System.Collections.Immutable;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Toolchain.Tests;

public sealed class RawInspectionToolchainPathResolverTests
{
    private static readonly string PackageRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "netwasm-toolchain"));
    private static readonly string NodePath = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "node"));

    [Fact]
    public void ResolveVerifiesTheCompleteClosureAndReturnsExecutionPaths()
    {
        var assets = new RecordingAssetResolver();
        var resolver = new RawInspectionToolchainPathResolver(assets);

        var result = resolver.Resolve(CreateExecutable(), CreateCompatibility());

        Assert.Equal(
            ToolchainPlatformAssetIds.RawInspectionClosure.ToArray(),
            assets.Requests.ToArray());
        Assert.Equal("NetWasm.Toolchain", result.PackageId);
        Assert.Equal("0.1.0-preview.23", result.PackageVersion);
        Assert.Equal(NodePath, result.NodePath);
        Assert.Equal(new Version(26, 8, 1), result.NodeVersion);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.RawInspectionCommand),
            result.InspectionCommandPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.BinaryenModule),
            result.BinaryenModulePath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.BinaryenWasmOpt),
            result.WasmOptPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.BinaryenWasmMerge),
            result.WasmMergePath);
    }

    [Fact]
    public void ConstructorAndResolveRejectNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RawInspectionToolchainPathResolver(null!));

        var resolver = new RawInspectionToolchainPathResolver(
            new RecordingAssetResolver());
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            null!, CreateCompatibility()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            CreateExecutable(), null!));
    }

    [Theory]
    [InlineData("wasm-tools", "node")]
    [InlineData("node", "wasm-tools")]
    public void ResolveRejectsNonNodeProductsBeforeAssetResolution(
        string executableToolId,
        string compatibilityToolId)
    {
        var assets = new RecordingAssetResolver();
        var resolver = new RawInspectionToolchainPathResolver(assets);

        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            CreateExecutable() with { ToolId = executableToolId },
            CreateCompatibility() with { ToolId = compatibilityToolId }));

        Assert.Empty(assets.Requests);
    }

    [Fact]
    public void ResolveRejectsRelativeNodePathBeforeAssetResolution()
    {
        var assets = new RecordingAssetResolver();
        var resolver = new RawInspectionToolchainPathResolver(assets);

        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            CreateExecutable() with { AbsolutePath = "node" },
            CreateCompatibility()));

        Assert.Empty(assets.Requests);
    }

    [Fact]
    public void ResolveRejectsMissingNodeVersionBeforeAssetResolution()
    {
        var assets = new RecordingAssetResolver();
        var resolver = new RawInspectionToolchainPathResolver(assets);

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            CreateExecutable(),
            CreateCompatibility() with { Version = null! }));

        Assert.Empty(assets.Requests);
    }

    [Fact]
    public void ResolveStopsAtTheFirstAssetFailure()
    {
        var failure = new InvalidDataException("asset failure");
        var failedId = ToolchainPlatformAssetIds.RawImportInspector;
        var assets = new RecordingAssetResolver(failedId, failure);
        var resolver = new RawInspectionToolchainPathResolver(assets);

        var exception = Assert.Throws<InvalidDataException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));

        Assert.Same(failure, exception);
        Assert.Equal(
            ToolchainPlatformAssetIds.RawInspectionClosure
                .TakeWhile(id => id != failedId)
                .Append(failedId),
            assets.Requests);
    }

    [Fact]
    public void ResolveRejectsNullAssetProduct()
    {
        var failedId = ToolchainPlatformAssetIds.RawInspectionCommandCore;
        var assets = new RecordingAssetResolver(failedId, null);
        var resolver = new RawInspectionToolchainPathResolver(assets);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));
    }

    [Theory]
    [InlineData("wrong", "NetWasm.Toolchain", "0.1.0-preview.23", "132.0.0", true)]
    [InlineData("binaryen.module", "", "0.1.0-preview.23", "132.0.0", true)]
    [InlineData("binaryen.module", "NetWasm.Toolchain", "", "132.0.0", true)]
    [InlineData("binaryen.module", "NetWasm.Toolchain", "0.1.0-preview.23", "", true)]
    [InlineData("binaryen.module", "NetWasm.Toolchain", "0.1.0-preview.23", "132.0.0", false)]
    public void ResolveRejectsMalformedAssetProduct(
        string returnedId,
        string packageId,
        string packageVersion,
        string assetVersion,
        bool absolutePath)
    {
        var failedId = ToolchainPlatformAssetIds.BinaryenModule;
        var product = new ResolvedPlatformAsset(
            packageId,
            packageVersion,
            returnedId,
            assetVersion,
            absolutePath ? AssetPath(failedId) : "binaryen/index.js");
        var resolver = new RawInspectionToolchainPathResolver(
            new RecordingAssetResolver(failedId, product));

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));
    }

    [Theory]
    [InlineData("Other.Toolchain", "0.1.0-preview.23")]
    [InlineData("NetWasm.Toolchain", "0.1.0-preview.20")]
    public void ResolveRejectsAssetsFromDifferentPackageIdentities(
        string packageId,
        string packageVersion)
    {
        var failedId = ToolchainPlatformAssetIds.BinaryenLicense;
        var product = CreateAsset(failedId) with
        {
            PackageId = packageId,
            PackageVersion = packageVersion,
        };
        var resolver = new RawInspectionToolchainPathResolver(
            new RecordingAssetResolver(failedId, product));

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));
    }

    private static ResolvedHostExecutable CreateExecutable() => new(
        HostToolIds.Node,
        NodePath,
        HostExecutableResolutionSource.Path);

    private static ValidatedHostToolCompatibility CreateCompatibility() => new(
        HostToolIds.Node,
        new Version(26, 8, 1),
        ImmutableArray<string>.Empty);

    private static ResolvedPlatformAsset CreateAsset(string id) => new(
        "NetWasm.Toolchain",
        "0.1.0-preview.23",
        id,
        id.StartsWith("binaryen.", StringComparison.Ordinal) ? "132.0.0" : "1",
        AssetPath(id));

    private static string AssetPath(string id) => Path.Combine(PackageRoot, id);

    private sealed class RecordingAssetResolver(
        string? selectedId = null,
        object? selectedResult = null) : IPinnedPlatformAssetPathResolver
    {
        private readonly List<string> _requests = [];

        public ImmutableArray<string> Requests => [.. _requests];

        public ResolvedPlatformAsset Resolve(string assetId)
        {
            _requests.Add(assetId);
            if (string.Equals(assetId, selectedId, StringComparison.Ordinal))
            {
                if (selectedResult is Exception exception)
                {
                    throw exception;
                }

                return (ResolvedPlatformAsset?)selectedResult!;
            }

            return CreateAsset(assetId);
        }
    }
}
