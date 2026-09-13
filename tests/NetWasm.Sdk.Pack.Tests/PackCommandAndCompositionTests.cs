namespace NetWasm.Sdk.Pack.Tests;

public sealed class PackCommandAndCompositionTests
{
    [Fact]
    public void CommandBuildsMainAndOptionalSymbolOutputsAndManifest()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var output = Path.Combine(directory.Path, "Sample.nupkg");
        var assembly = Path.Combine(directory.Path, "sample.dll");
        var symbols = Path.Combine(directory.Path, "sample.pdb");
        var nuspec = Path.Combine(directory.Path, "obj", "Sample.nuspec");
        var manifest = Path.Combine(directory.Path, "obj", "pack-manifest.json");
        File.WriteAllBytes(assembly, [1]);
        File.WriteAllBytes(symbols, [2]);
        var inputs = PackTestFixtures.Inputs(output) with
        {
            Files = [new CanonicalPackageFileInput(assembly, "lib/NetWasm,Version=v0.1/Sample.dll")],
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput(symbols, "lib/NetWasm,Version=v0.1/Sample.pdb")]),
            NuspecOutputPath = nuspec,
            ManifestOutputPath = manifest
        };
        var pathValidator = new PackagePathValidator();
        var nuspecWriter = new NuspecDocumentWriter();
        var fileReader = new LocalPackageFileReader(new LocalInputFileStreamOpener());
        var archiveWriter = new DeterministicArchiveWriter(new PackageValidator(pathValidator), new PackageArchiveOutputValidator(pathValidator));
        var command = new PackCommandTarget(
            PackTestFixtures.Builder(),
            new ArchiveEntryPlanner(nuspecWriter, fileReader, pathValidator),
            new PackOutputTransaction(
                archiveWriter,
                new SymbolPackageWriter(nuspecWriter, fileReader, archiveWriter, pathValidator),
                new PackManifestBuilder(new PackRequestFingerprintBuilder(fileReader)),
                new PackManifestSerializer(),
                new AtomicPackArtifactWriter(),
                new FileMover()));
        var result = command.BuildPackage(inputs);
        Assert.True(File.Exists(result.Package.Path));
        Assert.NotNull(result.Symbols);
        Assert.Equal(64, result.Manifest.RequestHash.Length);
        Assert.Equal(result.Package.Sha256, result.Manifest.PackageHash);
        Assert.True(File.Exists(nuspec));
        Assert.True(File.Exists(manifest));
        var manifestText = File.ReadAllText(manifest);
        Assert.Contains('\n', manifestText);
        Assert.DoesNotContain('\r', manifestText);
        var replacement = command.BuildPackage(inputs);
        Assert.Equal(result.Package.Sha256, replacement.Package.Sha256);
    }

    [Fact]
    public void ManifestBuilderIsAnIndependentCapability()
    {
        Assert.IsType<PackManifestBuilder>(new PackManifestBuilder(new PackRequestFingerprintBuilder(new InMemoryPackageFileReader(new Dictionary<string, byte[]>()))));
        Assert.Throws<ArgumentNullException>(() => new PackCommandTarget(null!, null!, null!));
        var builder = PackTestFixtures.Builder();
        var planner = new ArchiveEntryPlanner(new NuspecDocumentWriter(), new InMemoryPackageFileReader(new Dictionary<string, byte[]>()), new PackagePathValidator());
        var transaction = new PackOutputTransaction(
            new DeterministicArchiveWriter(new PackageValidator(new PackagePathValidator()), new PackageArchiveOutputValidator(new PackagePathValidator())),
            new SymbolPackageWriter(new NuspecDocumentWriter(), new InMemoryPackageFileReader(new Dictionary<string, byte[]>()), new DeterministicArchiveWriter(new PackageValidator(new PackagePathValidator()), new PackageArchiveOutputValidator(new PackagePathValidator())), new PackagePathValidator()),
            new PackManifestBuilder(new PackRequestFingerprintBuilder(new InMemoryPackageFileReader(new Dictionary<string, byte[]>()))),
            new PackManifestSerializer(), new AtomicPackArtifactWriter(), new FileMover());
        Assert.Throws<ArgumentNullException>(() => new PackCommandTarget(null!, planner, transaction));
        Assert.Throws<ArgumentNullException>(() => new PackCommandTarget(builder, null!, transaction));
        Assert.Throws<ArgumentNullException>(() => new PackCommandTarget(builder, planner, null!));
    }
}
