using System.IO.Compression;
using System.Text;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class SymbolPackageWriterTests
{
    [Fact]
    public void SymbolWriterCreatesIndependentSnupkgWithoutDependencies()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var output = Path.Combine(directory.Path, "Sample.nupkg");
        var package = PackTestFixtures.Builder().Build(PackTestFixtures.Inputs(output) with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("sample.pdb", "lib/NetWasm,Version=v0.1/Sample.pdb")])
        });
        var writer = new SymbolPackageWriter(
            new NuspecDocumentWriter(),
            new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.pdb"] = [4, 5] }),
            CreateArchiveWriter(),
            new PackagePathValidator());
        var result = writer.Write(package);
        Assert.NotNull(result);
        Assert.EndsWith(".snupkg", result!.Path, StringComparison.Ordinal);
        using var archive = ZipFile.OpenRead(result.Path);
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("Sample.pdb", StringComparison.Ordinal));
        var nuspec = archive.GetEntry("NetWasm.Sample.nuspec");
        Assert.NotNull(nuspec);
        using var reader = new StreamReader(nuspec!.Open(), Encoding.UTF8);
        Assert.DoesNotContain("<dependencies>", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void SymbolWriterReturnsNothingWhenDisabled()
    {
        var package = PackTestFixtures.Package("/tmp/sample.nupkg");
        var writer = new SymbolPackageWriter(
            new NuspecDocumentWriter(),
            new InMemoryPackageFileReader(new Dictionary<string, byte[]>()),
            CreateArchiveWriter(),
            new PackagePathValidator());
        Assert.Null(writer.Write(package));
        var archive = CreateArchiveWriter();
        var reader = new InMemoryPackageFileReader(new Dictionary<string, byte[]>());
        var nuspec = new NuspecDocumentWriter();
        var path = new PackagePathValidator();
        Assert.Throws<ArgumentNullException>(() => new SymbolPackageWriter(null!, reader, archive, path));
        Assert.Throws<ArgumentNullException>(() => new SymbolPackageWriter(nuspec, null!, archive, path));
        Assert.Throws<ArgumentNullException>(() => new SymbolPackageWriter(nuspec, reader, null!, path));
        Assert.Throws<ArgumentNullException>(() => new SymbolPackageWriter(nuspec, reader, archive, null!));
    }

    [Fact]
    public void SymbolWriterRejectsArchiveLimitBeforeReadingAnySymbol()
    {
        var reader = new RecordingReader([1, 2, 3]);
        var package = PackTestFixtures.Package("sample.nupkg") with
        {
            Determinism = DeterminismPolicy.Default with { MaxArchiveBytes = 5 },
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("symbol", "lib/a.pdb")])
        };

        var error = Assert.Throws<NetWasmPackException>(() => new SymbolPackageWriter(
            new NuspecDocumentWriter(), reader, CreateArchiveWriter(), new PackagePathValidator()).Write(package));

        Assert.Equal(NetWasmPackErrorCode.NWPK014, error.Code);
        Assert.Empty(reader.Requests);
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 10, 0)]
    public void SymbolWriterRejectsInvalidArchivePolicy(int maxEntries, long maxEntryBytes, long maxArchiveBytes)
    {
        var package = PackTestFixtures.Package("sample.nupkg") with
        {
            Determinism = DeterminismPolicy.Default with
            {
                MaxEntries = maxEntries,
                MaxEntryBytes = maxEntryBytes,
                MaxArchiveBytes = maxArchiveBytes
            },
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("a", "lib/a.pdb")])
        };

        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => CreateWriter().Write(package)).Code);
    }

    [Fact]
    public void SymbolWriterRejectsWhenEnvelopeConsumesTheWholeArchiveBudget()
    {
        var initial = PackTestFixtures.Package("sample.nupkg") with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("symbol", "lib/a.pdb")])
        };
        var envelopeBytes = 0;
        var package = initial with { Determinism = DeterminismPolicy.Default };
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var probe = package with { SymbolOutputPath = Path.Combine(directory.Path, "probe.snupkg") };
        var probeWriter = new SymbolPackageWriter(
            new NuspecDocumentWriter(), new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["symbol"] = [1] }),
            CreateArchiveWriter(), new PackagePathValidator());
        var output = probeWriter.Write(probe);
        Assert.NotNull(output);
        using (var archive = ZipFile.OpenRead(output!.Path))
        {
            envelopeBytes = archive.Entries
                .Where(entry => !entry.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                .Sum(entry => (int)entry.Length);
        }

        var limited = package with { Determinism = DeterminismPolicy.Default with { MaxArchiveBytes = envelopeBytes } };
        var limitedWriter = new SymbolPackageWriter(
            new NuspecDocumentWriter(), new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["symbol"] = [1] }),
            CreateArchiveWriter(), new PackagePathValidator());
        var error = Assert.Throws<NetWasmPackException>(() => limitedWriter.Write(limited));

        Assert.Equal(NetWasmPackErrorCode.NWPK014, error.Code);
    }

    [Fact]
    public void SymbolWriterRejectsUnsupportedFormatMissingInputAndUnsafePath()
    {
        var unsupported = PackTestFixtures.Package("/tmp/sample.nupkg") with { Symbols = new SymbolInputs(true, "symbols.nupkg", [new SymbolInput("a", "lib/a.pdb")]) };
        var writer = CreateWriter();
        Assert.Equal(NetWasmPackErrorCode.NWPK012, Assert.Throws<NetWasmPackException>(() => writer.Write(unsupported)).Code);
        var missing = unsupported with { Symbols = new SymbolInputs(true, "snupkg", []) };
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => writer.Write(missing)).Code);
        var unsafePath = unsupported with { Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("a", "../a.pdb")]) };
        Assert.Equal(NetWasmPackErrorCode.NWPK009, Assert.Throws<NetWasmPackException>(() => writer.Write(unsafePath)).Code);
    }

    [Fact]
    public void SymbolWriterRejectsInvalidExtensionDuplicateAndInputChanges()
    {
        var writer = CreateWriter();
        var package = PackTestFixtures.Package("/tmp/sample.nupkg") with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("a", "lib/a.txt")])
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK012, Assert.Throws<NetWasmPackException>(() => writer.Write(package)).Code);
        var duplicate = package with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("a", "NetWasm.Sample.nuspec")])
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK012, Assert.Throws<NetWasmPackException>(() => writer.Write(duplicate)).Code);
        var duplicateSymbols = package with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("a", "lib/a.pdb"), new SymbolInput("a", "lib/a.pdb")])
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK012, Assert.Throws<NetWasmPackException>(() => writer.Write(duplicateSymbols)).Code);
        var lengthMismatch = package with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("a", "lib/a.pdb") { ExpectedLength = 2 }])
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK010, Assert.Throws<NetWasmPackException>(() => writer.Write(lengthMismatch)).Code);
        var hashMismatch = package with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("a", "lib/a.pdb") { ExpectedSha256 = "00" }])
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK010, Assert.Throws<NetWasmPackException>(() => writer.Write(hashMismatch)).Code);
        var missing = package with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("missing", "lib/a.pdb")])
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => writer.Write(missing)).Code);

        var genericReaderWriter = new SymbolPackageWriter(
            new NuspecDocumentWriter(),
            new ThrowingReader(),
            CreateArchiveWriter(),
            new PackagePathValidator());
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => genericReaderWriter.Write(package with
        {
            Symbols = new SymbolInputs(true, "snupkg", [new SymbolInput("a", "lib/a.pdb")])
        })).Code);
    }

    private static SymbolPackageWriter CreateWriter() => new(
        new NuspecDocumentWriter(),
        new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["a"] = [1] }),
        CreateArchiveWriter(),
        new PackagePathValidator());

    private static DeterministicArchiveWriter CreateArchiveWriter()
    {
        var pathValidator = new PackagePathValidator();
        return new DeterministicArchiveWriter(new PackageValidator(pathValidator), new PackageArchiveOutputValidator(pathValidator));
    }

    private sealed class ThrowingReader : IPackageFileReader
    {
        public byte[] Read(string sourcePath, long maxLength) => throw new IOException("symbol input");
    }

    private sealed class RecordingReader(byte[] content) : IPackageFileReader
    {
        public List<(string Path, long Limit)> Requests { get; } = [];

        public byte[] Read(string sourcePath, long maxLength)
        {
            Requests.Add((sourcePath, maxLength));
            return content;
        }
    }
}
