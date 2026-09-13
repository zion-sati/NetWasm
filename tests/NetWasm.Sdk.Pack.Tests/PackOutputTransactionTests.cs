namespace NetWasm.Sdk.Pack.Tests;

public sealed class PackOutputTransactionTests
{
    [Fact]
    public void FailedPublicationRestoresThePreviouslyCompletePackage()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var packagePath = Path.Combine(directory.Path, "Sample.nupkg");
        var blockedSymbolPath = Path.Combine(directory.Path, "Sample.snupkg");
        Directory.CreateDirectory(blockedSymbolPath);
        var oldPackage = new byte[] { 7, 7, 7 };
        File.WriteAllBytes(packagePath, oldPackage);

        var pathValidator = new PackagePathValidator();
        var fileReader = new InMemoryPackageFileReader(new Dictionary<string, byte[]>
        {
            ["sample.dll"] = [1],
            ["sample.pdb"] = [2]
        });
        var nuspecWriter = new NuspecDocumentWriter();
        var archiveWriter = new DeterministicArchiveWriter(
            new PackageValidator(pathValidator),
            new PackageArchiveOutputValidator(pathValidator));
        var package = PackTestFixtures.Package(packagePath) with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("sample.pdb", "lib/NetWasm,Version=v0.1/Sample.pdb")]),
            SymbolOutputPath = blockedSymbolPath
        };
        var plan = new ArchiveEntryPlanner(nuspecWriter, fileReader, pathValidator).Plan(package);
        var fingerprint = new PackRequestFingerprintBuilder(fileReader);
        IPackOutputTransaction transaction = ThroughTransactionContract(new PackOutputTransaction(
            archiveWriter,
            new SymbolPackageWriter(nuspecWriter, fileReader, archiveWriter, pathValidator),
            new PackManifestBuilder(fingerprint),
            new PackManifestSerializer(),
            new AtomicPackArtifactWriter(),
            new FileMover()));

        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => transaction.Commit(package, plan)).Code);
        Assert.Equal(oldPackage, File.ReadAllBytes(packagePath));
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".netwasm-pack-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void MidPublicationFailureRestoresEveryPreviouslyCompleteOutputAndLeavesNoResidue()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var packagePath = Path.Combine(directory.Path, "Sample.nupkg");
        var symbolPath = Path.Combine(directory.Path, "Sample.snupkg");
        var nuspecPath = Path.Combine(directory.Path, "Sample.nuspec");
        var manifestPath = Path.Combine(directory.Path, "Sample.manifest.json");
        var priorOutputs = new Dictionary<string, byte[]>
        {
            [packagePath] = [7, 7, 7],
            [symbolPath] = [8, 8, 8],
            [nuspecPath] = [9, 9, 9],
            [manifestPath] = [10, 10, 10]
        };
        foreach (var output in priorOutputs)
        {
            File.WriteAllBytes(output.Key, output.Value);
        }

        var pathValidator = new PackagePathValidator();
        var fileReader = new InMemoryPackageFileReader(new Dictionary<string, byte[]>
        {
            ["sample.dll"] = [1],
            ["sample.pdb"] = [2]
        });
        var nuspecWriter = new NuspecDocumentWriter();
        var archiveWriter = new DeterministicArchiveWriter(
            new PackageValidator(pathValidator),
            new PackageArchiveOutputValidator(pathValidator));
        var package = PackTestFixtures.Package(packagePath) with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("sample.pdb", "lib/NetWasm,Version=v0.1/Sample.pdb")]),
            NuspecOutputPath = nuspecPath,
            ManifestOutputPath = manifestPath
        };
        var plan = new ArchiveEntryPlanner(nuspecWriter, fileReader, pathValidator).Plan(package);
        var mover = new FailingFileMover(failAtMove: 4);
        IPackOutputTransaction transaction = ThroughTransactionContract(new PackOutputTransaction(
            archiveWriter,
            new SymbolPackageWriter(nuspecWriter, fileReader, archiveWriter, pathValidator),
            new PackManifestBuilder(new PackRequestFingerprintBuilder(fileReader)),
            new PackManifestSerializer(),
            new AtomicPackArtifactWriter(),
            mover));

        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => transaction.Commit(package, plan)).Code);
        foreach (var output in priorOutputs)
        {
            Assert.Equal(output.Value, File.ReadAllBytes(output.Key));
        }

        Assert.Equal(6, mover.MoveCount);
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".netwasm-pack-*", SearchOption.TopDirectoryOnly));
        Assert.DoesNotContain(Directory.EnumerateFiles(directory.Path), path => path.EndsWith(".previous", StringComparison.Ordinal));
    }

    [Fact]
    public void FileMoverMovesAndOverwritesOneOutput()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var source = Path.Combine(directory.Path, "source");
        var destination = Path.Combine(directory.Path, "destination");
        File.WriteAllBytes(source, [1]);
        File.WriteAllBytes(destination, [2]);

        IFileMover mover = ThroughFileMoverContract(new FileMover());
        mover.Move(source, destination, overwrite: true);

        Assert.Equal([1], File.ReadAllBytes(destination));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void TransactionRejectsDuplicateOutputDestinationsBeforeStaging()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var output = Path.Combine(directory.Path, "Sample.nupkg");
        var package = PackTestFixtures.Package(output) with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("sample.pdb", "lib/NetWasm,Version=v0.1/Sample.pdb")]),
            SymbolOutputPath = output
        };
        var pathValidator = new PackagePathValidator();
        var fileReader = new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1], ["sample.pdb"] = [2] });
        var planner = new ArchiveEntryPlanner(new NuspecDocumentWriter(), fileReader, pathValidator);
        var archiveWriter = new DeterministicArchiveWriter(new PackageValidator(pathValidator), new PackageArchiveOutputValidator(pathValidator));
        IPackOutputTransaction transaction = ThroughTransactionContract(new PackOutputTransaction(
            archiveWriter,
            new SymbolPackageWriter(new NuspecDocumentWriter(), fileReader, archiveWriter, pathValidator),
            new PackManifestBuilder(new PackRequestFingerprintBuilder(fileReader)),
            new PackManifestSerializer(),
            new AtomicPackArtifactWriter(),
            new FileMover()));
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => transaction.Commit(package, planner.Plan(package))).Code);
    }

    [Fact]
    public void TransactionMapsInjectedPackFailuresAndFilesystemFailures()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var package = PackTestFixtures.Package(Path.Combine(directory.Path, "Sample.nupkg"));
        var plan = new ArchivePlan("unused", [], DeterminismPolicy.Default, package.Identity);
        IPackOutputTransaction dependencies = ThroughTransactionContract(new PackOutputTransaction(
            new ThrowingArchiveWriter(new NetWasmPackException(NetWasmPackErrorCode.NWPK010, "input")),
            new NullSymbolWriter(),
            new FixedManifestBuilder(),
            new PackManifestSerializer(),
            new AtomicPackArtifactWriter(),
            new FileMover()));
        Assert.Equal(NetWasmPackErrorCode.NWPK010, Assert.Throws<NetWasmPackException>(() => dependencies.Commit(package, plan)).Code);

        IPackOutputTransaction ioFailure = ThroughTransactionContract(new PackOutputTransaction(
            new ThrowingArchiveWriter(new IOException("archive")),
            new NullSymbolWriter(),
            new FixedManifestBuilder(),
            new PackManifestSerializer(),
            new AtomicPackArtifactWriter(),
            new FileMover()));
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => ioFailure.Commit(package, plan)).Code);
    }

    [Fact]
    public void TransactionPublishesACompletePackageSetWithoutSymbols()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var packagePath = Path.Combine(directory.Path, "Sample.nupkg");
        var package = PackTestFixtures.Package(packagePath) with
        {
            NuspecOutputPath = Path.Combine(directory.Path, "Sample.nuspec"),
            ManifestOutputPath = Path.Combine(directory.Path, "Sample.manifest.json")
        };
        File.WriteAllBytes(package.OutputPath, [9]);
        File.WriteAllBytes(package.NuspecOutputPath!, [9]);
        File.WriteAllBytes(package.ManifestOutputPath!, [9]);
        var pathValidator = new PackagePathValidator();
        var reader = new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1] });
        var nuspec = new NuspecDocumentWriter();
        var archive = new DeterministicArchiveWriter(new PackageValidator(pathValidator), new PackageArchiveOutputValidator(pathValidator));
        var plan = new ArchiveEntryPlanner(nuspec, reader, pathValidator).Plan(package);
        IPackOutputTransaction transaction = ThroughTransactionContract(new PackOutputTransaction(
            archive,
            new SymbolPackageWriter(nuspec, reader, archive, pathValidator),
            new PackManifestBuilder(new PackRequestFingerprintBuilder(reader)),
            new PackManifestSerializer(),
            new AtomicPackArtifactWriter(),
            new FileMover()));

        var result = transaction.Commit(package, plan);
        Assert.Null(result.Symbols);
        Assert.True(File.Exists(packagePath));
        Assert.True(File.Exists(package.NuspecOutputPath));
        Assert.True(File.Exists(package.ManifestOutputPath));
    }

    [Fact]
    public void TransactionRollsBackWhenAStagedOutputIsMissingAndRejectsInvalidPath()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var package = PackTestFixtures.Package(Path.Combine(directory.Path, "Sample.nupkg"));
        IPackOutputTransaction transaction = ThroughTransactionContract(new PackOutputTransaction(
            new MissingArchiveWriter(),
            new NullSymbolWriter(),
            new FixedManifestBuilder(),
            new PackManifestSerializer(),
            new AtomicPackArtifactWriter(),
            new FileMover()));
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => transaction.Commit(package, new ArchivePlan("unused", [], DeterminismPolicy.Default, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => transaction.Commit(package with { OutputPath = "\u0000" }, new ArchivePlan("unused", [], DeterminismPolicy.Default, package.Identity))).Code);
    }

    [Fact]
    public void TransactionRequiresEveryCollaborator()
    {
        var archive = new MissingArchiveWriter();
        var symbols = new NullSymbolWriter();
        var manifest = new FixedManifestBuilder();
        var serializer = new PackManifestSerializer();
        var artifacts = new AtomicPackArtifactWriter();
        Assert.Throws<ArgumentNullException>(() => new PackOutputTransaction(null!, symbols, manifest, serializer, artifacts, new FileMover()));
        Assert.Throws<ArgumentNullException>(() => new PackOutputTransaction(archive, null!, manifest, serializer, artifacts, new FileMover()));
        Assert.Throws<ArgumentNullException>(() => new PackOutputTransaction(archive, symbols, null!, serializer, artifacts, new FileMover()));
        Assert.Throws<ArgumentNullException>(() => new PackOutputTransaction(archive, symbols, manifest, null!, artifacts, new FileMover()));
        Assert.Throws<ArgumentNullException>(() => new PackOutputTransaction(archive, symbols, manifest, serializer, null!, new FileMover()));
        Assert.Throws<ArgumentNullException>(() => new PackOutputTransaction(archive, symbols, manifest, serializer, artifacts, null!));
    }

    private sealed class MissingArchiveWriter : IArchiveWriter
    {
        public PackageOutput Write(ArchivePlan plan) => new(Path.Combine(Path.GetDirectoryName(plan.OutputPath) ?? Path.GetTempPath(), "missing.nupkg"), "hash", plan.Identity);
    }

    private sealed class ThrowingArchiveWriter(Exception failure) : IArchiveWriter
    {
        public PackageOutput Write(ArchivePlan plan) => throw failure;
    }

    private sealed class NullSymbolWriter : ISymbolPackageWriter
    {
        public SymbolPackageOutput? Write(CanonicalPackage package) => null;
    }

    private sealed class FixedManifestBuilder : IPackManifestBuilder
    {
        public CanonicalPackManifest Build(CanonicalPackage package, ArchivePlan plan, PackageOutput output, SymbolPackageOutput? symbols) =>
            new("1", "0.1", "request", [], output.Sha256, symbols?.Sha256);
    }

    private sealed class FailingFileMover(int failAtMove) : IFileMover
    {
        public int MoveCount { get; private set; }

        public void Move(string sourcePath, string destinationPath, bool overwrite)
        {
            MoveCount++;
            if (MoveCount == failAtMove)
            {
                throw new IOException("deterministic publication failure");
            }

            File.Move(sourcePath, destinationPath, overwrite);
        }
    }

    private static IPackOutputTransaction ThroughTransactionContract(IPackOutputTransaction transaction) => transaction;

    private static IFileMover ThroughFileMoverContract(IFileMover mover) => mover;
}
