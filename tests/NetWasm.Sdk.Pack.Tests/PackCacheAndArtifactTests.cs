using System.Text;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class PackCacheAndArtifactTests
{
    [Fact]
    public void CacheValidatorAcceptsMatchingNoBuildManifest()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var manifest = Path.Combine(directory.Path, "manifest.json");
        var package = Path.Combine(directory.Path, "sample.nupkg");
        File.WriteAllBytes(package, [1, 2, 3]);
        using var stream = File.OpenRead(package);
        var packageHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        File.WriteAllText(manifest, $"{{\"RequestHash\":\"abc\",\"PackageHash\":\"{packageHash}\"}}");
        var inputs = PackTestFixtures.Inputs(outputPath: package) with { NoBuild = true, ManifestOutputPath = manifest, ExpectedRequestHash = "ABC" };
        new PackCacheValidator(new FixedFingerprintBuilder("abc")).Validate(inputs, PackTestFixtures.Package(package));
    }

    [Fact]
    public void CacheValidatorRejectsMissingStaleAndMalformedNoBuildManifest()
    {
        var validator = new PackCacheValidator(new FixedFingerprintBuilder("abc"));
        var snapshot = PackTestFixtures.Package("/tmp/sample.nupkg");
        var missing = PackTestFixtures.Inputs() with { NoBuild = true, ManifestOutputPath = "/tmp/missing-manifest.json" };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(missing, snapshot)).Code);
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var manifest = Path.Combine(directory.Path, "manifest.json");
        var package = Path.Combine(directory.Path, "sample.nupkg");
        File.WriteAllBytes(package, [1]);
        File.WriteAllText(manifest, "{\"RequestHash\":\"abc\",\"PackageHash\":\"bad\"}");
        var mismatch = missing with { OutputPath = package, ManifestOutputPath = manifest, ExpectedRequestHash = "different" };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(mismatch, snapshot)).Code);
        File.WriteAllText(manifest, "not json");
        var malformed = mismatch with { ExpectedRequestHash = null };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(malformed, snapshot)).Code);
        File.WriteAllText(manifest, "{\"PackageHash\":\"hash\"}");
        var missingRequest = mismatch with { ExpectedRequestHash = null };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(missingRequest, snapshot)).Code);
    }

    [Fact]
    public void CacheValidatorRejectsStalePackageAndSymbolOutputs()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var package = Path.Combine(directory.Path, "sample.nupkg");
        var symbols = Path.Combine(directory.Path, "sample.snupkg");
        var manifest = Path.Combine(directory.Path, "manifest.json");
        File.WriteAllBytes(package, [1]);
        File.WriteAllBytes(symbols, [2]);
        File.WriteAllText(manifest, "{\"RequestHash\":\"abc\",\"PackageHash\":\"bad\",\"SymbolPackageHash\":\"bad\"}");
        var inputs = PackTestFixtures.Inputs(package) with
        {
            NoBuild = true,
            ManifestOutputPath = manifest,
            SymbolOutputPath = symbols,
            Symbols = new SymbolInputs(true, "snupkg", [])
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => new PackCacheValidator(new FixedFingerprintBuilder("abc")).Validate(inputs, PackTestFixtures.Package(package))).Code);

        var packageHash = Hash(package);
        var initialSymbolHash = Hash(symbols);
        File.WriteAllText(manifest, $"{{\"RequestHash\":\"abc\",\"PackageHash\":\"{packageHash}\",\"SymbolPackageHash\":\"{initialSymbolHash}\"}}");
        File.WriteAllBytes(symbols, [3]);
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => new PackCacheValidator(new FixedFingerprintBuilder("abc")).Validate(inputs, PackTestFixtures.Package(package))).Code);

        var symbolHash = Hash(symbols);
        File.WriteAllText(manifest, $"{{\"RequestHash\":\"abc\",\"PackageHash\":\"{packageHash}\",\"SymbolPackageHash\":\"{symbolHash}\"}}");
        var expectedMismatch = inputs with { ExpectedRequestHash = "different" };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => new PackCacheValidator(new FixedFingerprintBuilder("abc")).Validate(expectedMismatch, PackTestFixtures.Package(package))).Code);
    }

    [Fact]
    public void CacheValidatorRejectsChangedCurrentInputEvenWhenExpectedHashIsAbsent()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var packagePath = Path.Combine(directory.Path, "sample.nupkg");
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        var input = PackTestFixtures.Inputs(packagePath) with { ManifestOutputPath = manifestPath };
        var fileReader = new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1] });
        var package = PackTestFixtures.Builder().Build(input);
        var pathValidator = new PackagePathValidator();
        var nuspecWriter = new NuspecDocumentWriter();
        var plan = new ArchiveEntryPlanner(nuspecWriter, fileReader, pathValidator).Plan(package);
        var archiveWriter = new DeterministicArchiveWriter(new PackageValidator(pathValidator), new PackageArchiveOutputValidator(pathValidator));
        var output = archiveWriter.Write(plan);
        var fingerprint = new PackRequestFingerprintBuilder(fileReader);
        var manifest = new PackManifestBuilder(fingerprint).Build(package, plan, output, null);
        File.WriteAllBytes(manifestPath, new PackManifestSerializer().Serialize(manifest));

        new PackCacheValidator(fingerprint).Validate(input with { NoBuild = true }, package);
        fileReader = new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [9] });
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() =>
            new PackCacheValidator(new PackRequestFingerprintBuilder(fileReader)).Validate(input with { NoBuild = true }, package)).Code);
    }

    [Fact]
    public void CacheValidatorExercisesEveryManifestAndSymbolCondition()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var packagePath = Path.Combine(directory.Path, "sample.nupkg");
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        var symbolsPath = Path.Combine(directory.Path, "sample.snupkg");
        File.WriteAllBytes(packagePath, [1]);
        File.WriteAllBytes(symbolsPath, [2]);
        var packageHash = Hash(packagePath);
        var symbolHash = Hash(symbolsPath);
        var validator = new PackCacheValidator(new FixedFingerprintBuilder("abc"));
        var input = PackTestFixtures.Inputs(packagePath) with { NoBuild = true, ManifestOutputPath = manifestPath };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(input with { ManifestOutputPath = " " }, PackTestFixtures.Package(packagePath))).Code);
        File.WriteAllText(manifestPath, "{\"RequestHash\":\"abc\"}");
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(input, PackTestFixtures.Package(packagePath))).Code);
        File.WriteAllText(manifestPath, "{\"RequestHash\":\"abc\",\"PackageHash\":\"\"}");
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(input, PackTestFixtures.Package(packagePath))).Code);
        var missingPackage = input with { OutputPath = Path.Combine(directory.Path, "missing.nupkg") };
        File.WriteAllText(manifestPath, $"{{\"RequestHash\":\"abc\",\"PackageHash\":\"{packageHash}\"}}");
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(missingPackage, PackTestFixtures.Package(missingPackage.OutputPath))).Code);

        var symbols = input with { Symbols = new SymbolInputs(true, "snupkg", []), SymbolOutputPath = symbolsPath };
        File.WriteAllText(manifestPath, $"{{\"RequestHash\":\"abc\",\"PackageHash\":\"{packageHash}\"}}");
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(symbols with { SymbolOutputPath = " " }, PackTestFixtures.Package(packagePath))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(symbols with { SymbolOutputPath = Path.Combine(directory.Path, "missing.snupkg") }, PackTestFixtures.Package(packagePath))).Code);
        File.WriteAllText(manifestPath, $"{{\"RequestHash\":\"abc\",\"PackageHash\":\"{packageHash}\"}}");
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(symbols, PackTestFixtures.Package(packagePath))).Code);
        File.WriteAllText(manifestPath, $"{{\"RequestHash\":\"abc\",\"PackageHash\":\"{packageHash}\",\"SymbolPackageHash\":\"{symbolHash}\"}}");
        validator.Validate(symbols, PackTestFixtures.Package(packagePath));
        validator.Validate(symbols with { Symbols = null! }, PackTestFixtures.Package(packagePath));
        File.WriteAllText(manifestPath, $"{{\"RequestHash\":\"abc\",\"PackageHash\":\"{packageHash}\",\"SymbolPackageHash\":\"{symbolHash}\"}}");
        validator.Validate(symbols, PackTestFixtures.Package(packagePath));
    }

    [Fact]
    public void ArtifactWriterAtomicallyPersistsNuspecAndManifestAndCanSkipOptionalPaths()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var nuspec = Path.Combine(directory.Path, "obj", "Sample.nuspec");
        var manifest = Path.Combine(directory.Path, "obj", "manifest.json");
        var writer = new AtomicPackArtifactWriter();
        writer.Write(new PackArtifacts(nuspec, Encoding.UTF8.GetBytes("nuspec"), manifest, Encoding.UTF8.GetBytes("manifest")));
        Assert.Equal("nuspec", File.ReadAllText(nuspec));
        Assert.Equal("manifest", File.ReadAllText(manifest));
        writer.Write(new PackArtifacts(null, [], null, []));
    }

    [Fact]
    public void ArtifactWriterRejectsCollisionsAndCleansItsStagingOnFailure()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var writer = new AtomicPackArtifactWriter();
        var same = Path.Combine(directory.Path, "same");
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => writer.Write(new PackArtifacts(same, [1], same, [2]))).Code);

        var valid = Path.Combine(directory.Path, "valid.nuspec");
        var blockedDirectory = Path.Combine(directory.Path, "blocked");
        Directory.CreateDirectory(blockedDirectory);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => writer.Write(new PackArtifacts(valid, [1], blockedDirectory, [2]))).Code);
        Assert.False(File.Exists(valid));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => writer.Write(new PackArtifacts("\u0000", [1], null, []))).Code);
    }

    [Fact]
    public void ManifestSerializerProducesStableBytes()
    {
        var manifest = new CanonicalPackManifest("1", "0.1", "request", ["a=hash"], "package", null);
        Assert.Equal(new PackManifestSerializer().Serialize(manifest), new PackManifestSerializer().Serialize(manifest));
        Assert.Throws<ArgumentNullException>(() => new PackManifestSerializer().Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => new PackCacheValidator(null!));
        Assert.Throws<ArgumentNullException>(() => new PackRequestFingerprintBuilder(null!));
        Assert.Throws<ArgumentNullException>(() => new PackManifestBuilder(null!));
    }

    [Fact]
    public void FingerprintIncludesSymbolsSourcesMetadataAndRestoreGraphSafely()
    {
        var reader = new InMemoryPackageFileReader(new Dictionary<string, byte[]>
        {
            ["sample.dll"] = [1],
            ["sample.pdb"] = [2],
            ["source.cs"] = [3]
        });
        var package = PackTestFixtures.Package("/private/user/sample.nupkg") with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("sample.pdb", "symbols/sample.pdb")]),
            Source = new SourceInputs(true, [new SourceInput("source.cs", "src/source.cs")], true, "{\"documents\":[]}"),
            Metadata = new PackageMetadata(
                "authors",
                "description",
                RepositoryUrl: "https://private.invalid/repository",
                RepositoryBranch: "refs/heads/main",
                RepositoryCommit: "0123456789abcdef0123456789abcdef01234567",
                PublishRepositoryUrl: true),
            NuspecOutputPath = "/private/user/sample.nuspec",
            ManifestOutputPath = "/private/user/sample.manifest.json",
            Restore = new RestoreEvidence("/private/user/project.assets.json", "assets-hash", "/private/user/packages.lock.json", "lock-hash", [CanonicalPackPlanBuilder.CanonicalTargetFramework], "7.6.0", "10.0.0")
            {
                Required = true,
                Graph = [new RestoreDependencyEvidence("Dependency", "1.0.0", CanonicalPackPlanBuilder.CanonicalTargetFramework, false, false)
                {
                    VersionRange = "[1.0.0]", ResolvedVersion = "1.0.0", Source = "Producer/1.0.0"
                }]
            }
        };
        var fingerprint = new PackRequestFingerprintBuilder(reader).Build(package);
        Assert.Equal(64, fingerprint.Length);
        var privateMetadata = package with { Metadata = package.Metadata with { RepositoryUrl = "https://different.invalid/repository" } };
        Assert.NotEqual(fingerprint, new PackRequestFingerprintBuilder(reader).Build(privateMetadata));
        var differentCommit = package with { Metadata = package.Metadata with { RepositoryCommit = "fedcba9876543210fedcba9876543210fedcba98" } };
        Assert.NotEqual(fingerprint, new PackRequestFingerprintBuilder(reader).Build(differentCommit));
        var unpublished = package with
        {
            Metadata = package.Metadata with
            {
                PublishRepositoryUrl = false,
                RepositoryUrl = "https://different.invalid/repository",
                RepositoryBranch = "refs/heads/release",
                RepositoryCommit = "fedcba9876543210fedcba9876543210fedcba98"
            }
        };
        var unpublishedAgain = package with
        {
            Metadata = package.Metadata with
            {
                PublishRepositoryUrl = false,
                RepositoryUrl = "https://private.invalid/repository"
            }
        };
        Assert.Equal(new PackRequestFingerprintBuilder(reader).Build(unpublished), new PackRequestFingerprintBuilder(reader).Build(unpublishedAgain));
    }

    [Fact]
    public void FingerprintRejectsCumulativeArchiveLimitBeforeReadingLaterFiles()
    {
        var reader = new RecordingReader(new Dictionary<string, byte[]>
        {
            ["first.dll"] = [1],
            ["second.dll"] = [2]
        });
        var package = PackTestFixtures.Package("sample.nupkg") with
        {
            Files =
            [
                new CanonicalPackageFile("first.dll", "lib/NetWasm,Version=v0.1/First.dll"),
                new CanonicalPackageFile("second.dll", "lib/NetWasm,Version=v0.1/Second.dll")
            ],
            Determinism = DeterminismPolicy.Default with { MaxArchiveBytes = 1 }
        };

        var error = Assert.Throws<NetWasmPackException>(() => new PackRequestFingerprintBuilder(reader).Build(package));

        Assert.Equal(NetWasmPackErrorCode.NWPK014, error.Code);
        Assert.Single(reader.Requests);
        Assert.Equal(1, reader.Requests[0].Limit);
    }

    [Fact]
    public void FingerprintRejectsCumulativeSymbolLimitBeforeReadingLaterSymbols()
    {
        var reader = new RecordingReader(new Dictionary<string, byte[]>
        {
            ["first.pdb"] = [1],
            ["second.pdb"] = [2]
        });
        var package = PackTestFixtures.Package("sample.nupkg") with
        {
            Files = [],
            Symbols = new SymbolInputs(true, "snupkg", [
                new SymbolInput("first.pdb", "lib/first.pdb"),
                new SymbolInput("second.pdb", "lib/second.pdb")
            ]),
            Determinism = DeterminismPolicy.Default with { MaxArchiveBytes = 1 }
        };

        var error = Assert.Throws<NetWasmPackException>(() => new PackRequestFingerprintBuilder(reader).Build(package));

        Assert.Equal(NetWasmPackErrorCode.NWPK014, error.Code);
        Assert.Single(reader.Requests);
        Assert.Equal("first.pdb", reader.Requests[0].Path);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class FixedFingerprintBuilder(string hash) : IPackRequestFingerprintBuilder
    {
        public string Build(CanonicalPackage package) => hash;
    }

    private sealed class RecordingReader(IReadOnlyDictionary<string, byte[]> files) : IPackageFileReader
    {
        public List<(string Path, long Limit)> Requests { get; } = [];

        public byte[] Read(string sourcePath, long maxLength)
        {
            Requests.Add((sourcePath, maxLength));
            return files[sourcePath];
        }
    }
}
