using System.Collections.Immutable;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

#pragma warning disable CA1859 // Contract tests deliberately dispatch through the public interfaces.
namespace NetWasm.Toolchain.Tests;

public sealed class JcoToolchainPathResolverTests
{
    private static readonly string PackageRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "netwasm-toolchain-jco"));
    private static readonly string NodePath = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "node"));

    [Fact]
    public void ResolveVerifiesTheCanonicalClosureAndReturnsExecutionPaths()
    {
        var assets = new RecordingAssetResolver();
        var closureVerifier = new RecordingClosureIntegrityVerifier();
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            assets,
            closureVerifier);

        var result = resolver.Resolve(CreateExecutable(), CreateCompatibility());

        Assert.Equal(
            ToolchainPlatformAssetIds.JcoClosure.ToArray(),
            assets.Requests.ToArray());
        Assert.Equal("NetWasm.Toolchain", result.PackageId);
        Assert.Equal("0.2.0-preview.23", result.PackageVersion);
        Assert.Equal("1.28.1", result.JcoVersion);
        Assert.Equal("0.24.1", result.Preview2ShimVersion);
        Assert.Equal(NodePath, result.NodePath);
        Assert.Equal(new Version(25, 0), result.NodeVersion);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.JcoEntryPoint), result.EntryPointPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.JcoPackageLock), result.PackageLockPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.JcoClosureIntegrity), result.ClosureIntegrityPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.JcoNotices), result.NoticesPath);
        Assert.Equal(AssetPath(ToolchainPlatformAssetIds.JcoClosurePolicy), result.ClosurePolicyPath);
        Assert.Equal(PackageRoot, closureVerifier.PackageRoot);
        Assert.Equal(
            "tools/jco/closure-integrity.json",
            closureVerifier.ManifestRelativePath);
    }

    [Fact]
    public void ResolveDoesNotApplyANodeVersionPolicy()
    {
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            new RecordingAssetResolver(),
            new RecordingClosureIntegrityVerifier());

        var result = resolver.Resolve(
            CreateExecutable(),
            CreateCompatibility() with { Version = new Version(1, 0) });

        Assert.Equal(new Version(1, 0), result.NodeVersion);
    }

    [Fact]
    public void ConstructorRejectsInvalidDependenciesAndPackageRoot()
    {
        var assets = new RecordingAssetResolver();
        var verifier = new RecordingClosureIntegrityVerifier();

        Assert.Throws<ArgumentException>(() => new JcoToolchainPathResolver(
            "relative", assets, verifier));
        Assert.Throws<ArgumentNullException>(() => new JcoToolchainPathResolver(
            null!, assets, verifier));
        Assert.Throws<ArgumentNullException>(() => new JcoToolchainPathResolver(
            PackageRoot, null!, verifier));
        Assert.Throws<ArgumentNullException>(() => new JcoToolchainPathResolver(
            PackageRoot, assets, null!));
    }

    [Fact]
    public void ResolveRejectsNullInputsBeforeAssetResolution()
    {
        var assets = new RecordingAssetResolver();
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            assets,
            new RecordingClosureIntegrityVerifier());

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!, CreateCompatibility()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(CreateExecutable(), null!));
        Assert.Empty(assets.Requests);
    }

    [Theory]
    [InlineData("wasm-tools", "node")]
    [InlineData("node", "wasm-tools")]
    public void ResolveRejectsNonNodeProductsBeforeAssetResolution(
        string executableToolId,
        string compatibilityToolId)
    {
        var assets = new RecordingAssetResolver();
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            assets,
            new RecordingClosureIntegrityVerifier());

        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            CreateExecutable() with { ToolId = executableToolId },
            CreateCompatibility() with { ToolId = compatibilityToolId }));
        Assert.Empty(assets.Requests);
    }

    [Fact]
    public void ResolveRejectsRelativeNodePathBeforeAssetResolution()
    {
        var assets = new RecordingAssetResolver();
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            assets,
            new RecordingClosureIntegrityVerifier());

        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            CreateExecutable() with { AbsolutePath = "node" },
            CreateCompatibility()));
        Assert.Empty(assets.Requests);
    }

    [Fact]
    public void ResolveRejectsMissingNodeVersionBeforeAssetResolution()
    {
        var assets = new RecordingAssetResolver();
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            assets,
            new RecordingClosureIntegrityVerifier());

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            CreateExecutable(),
            CreateCompatibility() with { Version = null! }));
        Assert.Empty(assets.Requests);
    }

    [Fact]
    public void ResolveStopsAtTheFirstAssetFailure()
    {
        var failedId = ToolchainPlatformAssetIds.JcoEntryPoint;
        var failure = new InvalidDataException("asset failure");
        var assets = new RecordingAssetResolver(failedId, failure);
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            assets,
            new RecordingClosureIntegrityVerifier());

        var exception = Assert.Throws<InvalidDataException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));

        Assert.Same(failure, exception);
        Assert.Equal(
            ToolchainPlatformAssetIds.JcoClosure
                .TakeWhile(id => id != failedId)
                .Append(failedId),
            assets.Requests);
    }

    [Fact]
    public void ResolveRejectsAResolverReturningNoAsset()
    {
        var failedId = ToolchainPlatformAssetIds.JcoPackage;
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            new RecordingAssetResolver(failedId, null),
            new RecordingClosureIntegrityVerifier());

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));
    }

    [Theory]
    [InlineData("wrong", "NetWasm.Toolchain", "0.2.0-preview.23", "1.28.1", true)]
    [InlineData("jco.package", "", "0.2.0-preview.23", "1.28.1", true)]
    [InlineData("jco.package", "NetWasm.Toolchain", "", "1.28.1", true)]
    [InlineData("jco.package", "NetWasm.Toolchain", "0.2.0-preview.23", "", true)]
    [InlineData("jco.package", "NetWasm.Toolchain", "0.2.0-preview.23", "1.28.1", false)]
    public void ResolveRejectsMalformedAssetProducts(
        string returnedId,
        string packageId,
        string packageVersion,
        string assetVersion,
        bool absolutePath)
    {
        var failedId = ToolchainPlatformAssetIds.JcoPackage;
        var product = CreateAsset(failedId) with
        {
            Id = returnedId,
            PackageId = packageId,
            PackageVersion = packageVersion,
            Version = assetVersion,
            AbsolutePath = absolutePath ? AssetPath(failedId) : "tools/jco/package.json",
        };
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            new RecordingAssetResolver(failedId, product),
            new RecordingClosureIntegrityVerifier());

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));
    }

    [Theory]
    [InlineData("Other.Toolchain", "0.2.0-preview.23")]
    [InlineData("NetWasm.Toolchain", "0.2.0-preview.20")]
    public void ResolveRejectsAssetsFromDifferentPackageIdentities(
        string packageId,
        string packageVersion)
    {
        var failedId = ToolchainPlatformAssetIds.Preview2ShimPackage;
        var product = CreateAsset(failedId) with
        {
            PackageId = packageId,
            PackageVersion = packageVersion,
        };
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            new RecordingAssetResolver(failedId, product),
            new RecordingClosureIntegrityVerifier());

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));
    }

    [Fact]
    public void ResolveRejectsJcoPackageAndEntryPointVersionMismatch()
    {
        var failedId = ToolchainPlatformAssetIds.JcoEntryPoint;
        var product = CreateAsset(failedId) with { Version = "1.28.0" };
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            new RecordingAssetResolver(failedId, product),
            new RecordingClosureIntegrityVerifier());

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));
    }

    [Fact]
    public void ResolvePropagatesClosureVerificationFailure()
    {
        var failure = new InvalidDataException("closure changed");
        IJcoToolchainPathResolver resolver = new JcoToolchainPathResolver(
            PackageRoot,
            new RecordingAssetResolver(),
            new RecordingClosureIntegrityVerifier(failure));

        var exception = Assert.Throws<InvalidDataException>(() => resolver.Resolve(
            CreateExecutable(), CreateCompatibility()));

        Assert.Same(failure, exception);
    }

    private static ResolvedHostExecutable CreateExecutable() => new(
        HostToolIds.Node,
        NodePath,
        HostExecutableResolutionSource.Path);

    private static ValidatedHostToolCompatibility CreateCompatibility() => new(
        HostToolIds.Node,
        new Version(25, 0),
        ImmutableArray<string>.Empty);

    private static ResolvedPlatformAsset CreateAsset(string id)
    {
        var version = id switch
        {
            ToolchainPlatformAssetIds.JcoPackage => "1.28.1",
            ToolchainPlatformAssetIds.JcoEntryPoint => "1.28.1",
            ToolchainPlatformAssetIds.Preview2ShimPackage => "0.24.1",
            _ => "1",
        };
        return new(
            "NetWasm.Toolchain",
            "0.2.0-preview.23",
            id,
            version,
            AssetPath(id));
    }

    private static string AssetPath(string id) => id switch
    {
        ToolchainPlatformAssetIds.JcoPackage => Path.Combine(
            PackageRoot,
            "tools",
            "jco",
            "node_modules",
            "@bytecodealliance",
            "jco",
            "package.json"),
        ToolchainPlatformAssetIds.Preview2ShimPackage => Path.Combine(
            PackageRoot,
            "tools",
            "jco",
            "node_modules",
            "@bytecodealliance",
            "preview2-shim",
            "package.json"),
        ToolchainPlatformAssetIds.JcoEntryPoint => Path.Combine(
            PackageRoot,
            "tools",
            "jco",
            "node_modules",
            "@bytecodealliance",
            "jco",
            "dist",
            "jco.js"),
        ToolchainPlatformAssetIds.JcoPackageLock => Path.Combine(
            PackageRoot,
            "tools",
            "jco",
            "package-lock.json"),
        ToolchainPlatformAssetIds.JcoClosureIntegrity => Path.Combine(
            PackageRoot,
            "tools",
            "jco",
            "closure-integrity.json"),
        ToolchainPlatformAssetIds.JcoNotices => Path.Combine(
            PackageRoot,
            "tools",
            "jco",
            "notices.json"),
        ToolchainPlatformAssetIds.JcoClosurePolicy => Path.Combine(
            PackageRoot,
            "tools",
            "jco",
            "closure-policy.json"),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
    };

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

    private sealed class RecordingClosureIntegrityVerifier(Exception? failure = null) :
        IJcoClosureIntegrityVerifier
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
