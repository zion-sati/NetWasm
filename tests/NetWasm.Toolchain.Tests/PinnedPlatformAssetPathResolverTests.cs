using System.Collections.Immutable;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Toolchain.Tests;

public sealed class PinnedPlatformAssetPathResolverTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly string PackageRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "netwasm-toolchain-package"));
    private static readonly string AssetPath = Path.Combine(
        PackageRoot, "tools", "binaryen", "index.js");

    [Fact]
    public void ResolveReturnsExactPackageAndAssetIdentityAfterVerification()
    {
        var presence = new RecordingPresenceChecker(AssetPath);
        var verifier = new RecordingDigestVerifier();
        var resolver = CreateResolver(presence, verifier);

        var result = resolver.Resolve("binaryen-module");

        Assert.Equal("NetWasm.Toolchain", result.PackageId);
        Assert.Equal("0.1.0-preview.23", result.PackageVersion);
        Assert.Equal("binaryen-module", result.Id);
        Assert.Equal("132.0.0", result.Version);
        Assert.Equal(AssetPath, result.AbsolutePath);
        Assert.Equal(AssetPath, verifier.Path);
        Assert.Equal(Digest, verifier.Digest);
    }

    [Fact]
    public void ResolveRejectsUnknownOrBlankAssetBeforeCapabilities()
    {
        var presence = new RecordingPresenceChecker(AssetPath);
        var verifier = new RecordingDigestVerifier();
        var resolver = CreateResolver(presence, verifier);

        var exception = Assert.Throws<UnsupportedPlatformAssetException>(
            () => resolver.Resolve("unknown"));
        Assert.Equal("unknown", exception.AssetId);
        Assert.Throws<ArgumentException>(() => resolver.Resolve(""));
        Assert.Equal(0, presence.CallCount);
        Assert.Equal(0, verifier.CallCount);
    }

    [Fact]
    public void ResolveRejectsMissingAssetBeforeDigestVerification()
    {
        var presence = new RecordingPresenceChecker();
        var verifier = new RecordingDigestVerifier();
        var resolver = CreateResolver(presence, verifier);

        Assert.Throws<ToolchainAssetMissingException>(
            () => resolver.Resolve("binaryen-module"));

        Assert.Equal(1, presence.CallCount);
        Assert.Equal(0, verifier.CallCount);
    }

    [Fact]
    public void ResolvePropagatesDigestFailureWithoutReturningAProduct()
    {
        var failure = new InvalidDataException("changed asset");
        var resolver = CreateResolver(
            new RecordingPresenceChecker(AssetPath),
            new RecordingDigestVerifier(failure));

        var exception = Assert.Throws<InvalidDataException>(
            () => resolver.Resolve("binaryen-module"));

        Assert.Same(failure, exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    public void ConstructorRejectsInvalidPackageRoot(string packageRoot)
    {
        Assert.Throws<ArgumentException>(() => new PinnedPlatformAssetPathResolver(
            packageRoot,
            CreateManifest(),
            new RecordingPresenceChecker(),
            new RecordingDigestVerifier()));
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var manifest = CreateManifest();
        var presence = new RecordingPresenceChecker();
        var verifier = new RecordingDigestVerifier();

        Assert.Throws<ArgumentNullException>(() => new PinnedPlatformAssetPathResolver(
            null!, manifest, presence, verifier));
        Assert.Throws<ArgumentNullException>(() => new PinnedPlatformAssetPathResolver(
            PackageRoot, null!, presence, verifier));
        Assert.Throws<ArgumentNullException>(() => new PinnedPlatformAssetPathResolver(
            PackageRoot, manifest, null!, verifier));
        Assert.Throws<ArgumentNullException>(() => new PinnedPlatformAssetPathResolver(
            PackageRoot, manifest, presence, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2")]
    public void ConstructorRejectsUnsupportedSchema(string schemaVersion)
    {
        Assert.Throws<ArgumentException>(() => CreateResolver(
            new RecordingPresenceChecker(),
            new RecordingDigestVerifier(),
            CreateManifest() with { SchemaVersion = schemaVersion }));
    }

    [Theory]
    [InlineData("", "0.1.0-preview.23")]
    [InlineData("NetWasm.Toolchain", "")]
    public void ConstructorRejectsBlankPackageIdentity(string packageId, string packageVersion)
    {
        Assert.Throws<ArgumentException>(() => CreateResolver(
            new RecordingPresenceChecker(),
            new RecordingDigestVerifier(),
            CreateManifest() with
            {
                PackageId = packageId,
                PackageVersion = packageVersion,
            }));
    }

    [Fact]
    public void ConstructorRejectsMissingOrDuplicateAssets()
    {
        var empty = CreateManifest() with
        {
            Assets = [],
        };
        Assert.Throws<ArgumentException>(() => CreateResolver(
            new RecordingPresenceChecker(),
            new RecordingDigestVerifier(),
            empty));

        var manifest = CreateManifest();
        var duplicate = manifest with
        {
            Assets = manifest.Assets.Add(manifest.Assets[0]),
        };
        Assert.Throws<ArgumentException>(() => CreateResolver(
            new RecordingPresenceChecker(),
            new RecordingDigestVerifier(),
            duplicate));
    }

    [Theory]
    [InlineData("", "132.0.0", "tools/binaryen/index.js", Digest)]
    [InlineData("binaryen-module", "132.0.0", "tools/binaryen/index.js", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("binaryen-module", "", "tools/binaryen/index.js", Digest)]
    [InlineData("binaryen-module", "132.0.0", "", Digest)]
    [InlineData("binaryen-module", "132.0.0", "tools/binaryen/index.js", "short")]
    [InlineData("binaryen-module", "132.0.0", "tools/binaryen/index.js", "g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void ConstructorRejectsMalformedAsset(
        string id,
        string version,
        string relativePath,
        string sha256)
    {
        var descriptor = CreateDescriptor() with
        {
            Id = id,
            Version = version,
            RelativePath = relativePath,
            Sha256 = sha256,
        };

        Assert.Throws<ArgumentException>(() => CreateResolver(
            new RecordingPresenceChecker(),
            new RecordingDigestVerifier(),
            CreateManifest(descriptor)));
    }

    [Fact]
    public void ConstructorRejectsNullAsset()
    {
        var manifest = CreateManifest() with
        {
            Assets = [null!],
        };
        Assert.Throws<ArgumentNullException>(() => CreateResolver(
            new RecordingPresenceChecker(),
            new RecordingDigestVerifier(),
            manifest));
    }

    [Fact]
    public void ConstructorRejectsAbsoluteAndEscapingAssetPaths()
    {
        var absolute = CreateDescriptor() with
        {
            RelativePath = Path.Combine(PackageRoot, "asset.js"),
        };
        var escaping = CreateDescriptor() with
        {
            RelativePath = Path.Combine("..", "asset.js"),
        };
        var root = CreateDescriptor() with
        {
            RelativePath = ".",
        };

        Assert.Throws<ArgumentException>(() => CreateResolver(
            new RecordingPresenceChecker(), new RecordingDigestVerifier(),
            CreateManifest(absolute)));
        Assert.Throws<ArgumentException>(() => CreateResolver(
            new RecordingPresenceChecker(), new RecordingDigestVerifier(),
            CreateManifest(escaping)));
        Assert.Throws<ArgumentException>(() => CreateResolver(
            new RecordingPresenceChecker(), new RecordingDigestVerifier(),
            CreateManifest(root)));
    }

    [Fact]
    public void ResolveRejectsAnAssetSymlinkThatEscapesThePackageRoot()
    {
        using var package = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var outsideAsset = outside.Write("asset.js", "outside");
        var tools = package.Combine("tools");
        Directory.CreateDirectory(tools);
        Directory.CreateSymbolicLink(package.Combine("tools", "linked"), outside.Path);

        var descriptor = CreateDescriptor() with
        {
            RelativePath = "tools/linked/asset.js",
        };
        var resolver = new PinnedPlatformAssetPathResolver(
            package.Path,
            CreateManifest(descriptor),
            new FilePresenceChecker(),
            new RecordingDigestVerifier());

        var exception = Assert.Throws<InvalidDataException>(
            () => resolver.Resolve("binaryen-module"));
        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(outsideAsset));
    }

    [Fact]
    public void ResolveAcceptsARegularAssetPhysicallyInsideThePackageRoot()
    {
        using var package = new TemporaryDirectory();
        var assetPath = package.Write("tools/binaryen/index.js", "inside");
        var resolver = new PinnedPlatformAssetPathResolver(
            package.Path,
            CreateManifest(CreateDescriptor() with
            {
                RelativePath = "tools/binaryen/index.js",
            }),
            new FilePresenceChecker(),
            new RecordingDigestVerifier());

        var result = resolver.Resolve("binaryen-module");

        Assert.Equal(assetPath, result.AbsolutePath);
    }

    private static PinnedPlatformAssetPathResolver CreateResolver(
        IFilePresenceChecker presence,
        IArtifactDigestVerifier verifier,
        PinnedToolchainManifest? manifest = null) =>
        new PinnedPlatformAssetPathResolver(
            PackageRoot,
            manifest ?? CreateManifest(),
            presence,
            verifier);

    private static PinnedToolchainManifest CreateManifest(
        PinnedPlatformAssetDescriptor? descriptor = null) => new()
        {
            SchemaVersion = "1",
            PackageId = "NetWasm.Toolchain",
            PackageVersion = "0.1.0-preview.23",
            Assets = ImmutableArray.Create(descriptor ?? CreateDescriptor()),
        };

    private static PinnedPlatformAssetDescriptor CreateDescriptor() => new()
    {
        Id = "binaryen-module",
        Version = "132.0.0",
        RelativePath = "tools/binaryen/index.js",
        Sha256 = Digest,
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netwasm-toolchain-assets-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] segments) => System.IO.Path.Combine([Path, .. segments]);

        public string Write(string relativePath, string content)
        {
            var path = Combine(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class RecordingPresenceChecker(params string[] existingPaths) :
        IFilePresenceChecker
    {
        private readonly ImmutableHashSet<string> _existingPaths =
            existingPaths.ToImmutableHashSet(StringComparer.Ordinal);

        public int CallCount { get; private set; }

        public bool Exists(string path)
        {
            CallCount++;
            return _existingPaths.Contains(path);
        }
    }

    private sealed class RecordingDigestVerifier(Exception? failure = null) :
        IArtifactDigestVerifier
    {
        public int CallCount { get; private set; }

        public string? Path { get; private set; }

        public string? Digest { get; private set; }

        public void Verify(string path, string expectedSha256)
        {
            CallCount++;
            Path = path;
            Digest = expectedSha256;
            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
