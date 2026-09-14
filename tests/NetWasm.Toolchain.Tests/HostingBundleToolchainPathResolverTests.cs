using System.Collections.Immutable;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

#pragma warning disable CA1859 // Contract tests deliberately dispatch through public interfaces.
namespace NetWasm.Toolchain.Tests;

public sealed class HostingBundleToolchainPathResolverTests
{
    private static readonly string PackageRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "netwasm-toolchain-bundler"));
    private static readonly string NodePath = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "node"));

    [Fact]
    public void ResolveVerifiesTheExactClosureAndReturnsExecutionPaths()
    {
        var assets = new RecordingAssetResolver();
        var integrity = new RecordingIntegrityVerifier();
        IHostingBundleToolchainPathResolver resolver =
            new HostingBundleToolchainPathResolver(PackageRoot, assets, integrity);

        var result = resolver.Resolve(Executable(), Compatibility());

        Assert.Equal(
            ToolchainPlatformAssetIds.HostingBundleClosure.ToArray(),
            assets.Requests.ToArray());
        Assert.Equal("NetWasm.Toolchain", result.PackageId);
        Assert.Equal("0.1.0-preview.24", result.PackageVersion);
        Assert.Equal("1.2.4", result.RolldownVersion);
        Assert.Equal(NodePath, result.NodePath);
        Assert.Equal(new Version(26, 7), result.NodeVersion);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.HostingBundleCommand), result.CommandPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.RolldownEntryPoint), result.EntryPointPath);
        Assert.Equal(PackageRoot, integrity.PackageRoot);
        Assert.Equal("tools/bundler/closure-integrity.json", integrity.ManifestRelativePath);
    }

    [Fact]
    public void ResolveRejectsInvalidNodeProductsBeforeReadingAssets()
    {
        var assets = new RecordingAssetResolver();
        var resolver = new HostingBundleToolchainPathResolver(
            PackageRoot,
            assets,
            new RecordingIntegrityVerifier());

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!, Compatibility()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(Executable(), null!));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            Executable() with { ToolId = HostToolIds.WasmTools }, Compatibility()));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            Executable(), Compatibility() with { ToolId = HostToolIds.WasmTools }));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            Executable() with { AbsolutePath = "node" }, Compatibility()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            Executable(), Compatibility() with { Version = null! }));
        Assert.Empty(assets.Requests);
    }

    [Fact]
    public void ResolveRejectsSplitPackageIdentityAndRolldownVersion()
    {
        var splitIdentity = CreateAsset(ToolchainPlatformAssetIds.RolldownEntryPoint) with
        {
            PackageVersion = "0.1.0-preview.25",
        };
        Assert.Throws<InvalidOperationException>(() => CreateResolver(
            ToolchainPlatformAssetIds.RolldownEntryPoint,
            splitIdentity).Resolve(Executable(), Compatibility()));

        var splitVersion = CreateAsset(ToolchainPlatformAssetIds.RolldownEntryPoint) with
        {
            Version = "1.2.3",
        };
        Assert.Throws<InvalidOperationException>(() => CreateResolver(
            ToolchainPlatformAssetIds.RolldownEntryPoint,
            splitVersion).Resolve(Executable(), Compatibility()));
    }

    [Fact]
    public void ResolvePropagatesAssetAndIntegrityFailures()
    {
        var assetFailure = new InvalidDataException("asset changed");
        var assetException = Assert.Throws<InvalidDataException>(() => CreateResolver(
            ToolchainPlatformAssetIds.RolldownPackage,
            assetFailure).Resolve(Executable(), Compatibility()));
        Assert.Same(assetFailure, assetException);

        var integrityFailure = new InvalidDataException("closure changed");
        var integrityException = Assert.Throws<InvalidDataException>(() =>
            new HostingBundleToolchainPathResolver(
                PackageRoot,
                new RecordingAssetResolver(),
                new RecordingIntegrityVerifier(integrityFailure))
            .Resolve(Executable(), Compatibility()));
        Assert.Same(integrityFailure, integrityException);
    }

    [Fact]
    public void ConstructorRejectsInvalidDependencies()
    {
        var assets = new RecordingAssetResolver();
        var integrity = new RecordingIntegrityVerifier();

        Assert.Throws<ArgumentException>(() => new HostingBundleToolchainPathResolver(
            "relative", assets, integrity));
        Assert.Throws<ArgumentNullException>(() => new HostingBundleToolchainPathResolver(
            null!, assets, integrity));
        Assert.Throws<ArgumentNullException>(() => new HostingBundleToolchainPathResolver(
            PackageRoot, null!, integrity));
        Assert.Throws<ArgumentNullException>(() => new HostingBundleToolchainPathResolver(
            PackageRoot, assets, null!));
    }

    private static HostingBundleToolchainPathResolver CreateResolver(
        string selectedId,
        object selectedResult) => new(
            PackageRoot,
            new RecordingAssetResolver(selectedId, selectedResult),
            new RecordingIntegrityVerifier());

    private static ResolvedHostExecutable Executable() => new(
        HostToolIds.Node,
        NodePath,
        HostExecutableResolutionSource.Path);

    private static ValidatedHostToolCompatibility Compatibility() => new(
        HostToolIds.Node,
        new Version(26, 7),
        ImmutableArray<string>.Empty);

    private static ResolvedPlatformAsset CreateAsset(string id) => new(
        "NetWasm.Toolchain",
        "0.1.0-preview.24",
        id,
        id is ToolchainPlatformAssetIds.RolldownPackage
            or ToolchainPlatformAssetIds.RolldownEntryPoint ? "1.2.4" : "1",
        AssetPath(id));

    private static string AssetPath(string id) => Path.Combine(
        PackageRoot,
        id switch
        {
            ToolchainPlatformAssetIds.HostingBundleCommand => "tools/bundler/netwasm-bundle.mjs",
            ToolchainPlatformAssetIds.RolldownPackage => "tools/bundler/node_modules/rolldown/package.json",
            ToolchainPlatformAssetIds.RolldownEntryPoint => "tools/bundler/node_modules/rolldown/dist/index.mjs",
            ToolchainPlatformAssetIds.HostingBundlePackageLock => "tools/bundler/package-lock.json",
            ToolchainPlatformAssetIds.HostingBundleClosureIntegrity => "tools/bundler/closure-integrity.json",
            ToolchainPlatformAssetIds.HostingBundleNotices => "tools/bundler/notices.json",
            ToolchainPlatformAssetIds.HostingBundleClosurePolicy => "tools/bundler/closure-policy.json",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
        });

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

    private sealed class RecordingIntegrityVerifier(Exception? failure = null) :
        IHostingBundleClosureIntegrityVerifier
    {
        public string? PackageRoot { get; private set; }
        public string? ManifestRelativePath { get; private set; }

        public void Verify(string packageRoot, string manifestRelativePath)
        {
            PackageRoot = packageRoot;
            ManifestRelativePath = manifestRelativePath;
            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}

#pragma warning restore CA1859
