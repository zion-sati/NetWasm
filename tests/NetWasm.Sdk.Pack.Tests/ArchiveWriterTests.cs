using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class ArchiveWriterTests
{
    [Fact]
    public void PlannerCreatesStockReadableCanonicalArchivePlan()
    {
        var package = PackTestFixtures.Package("/tmp/sample.nupkg");
        var planner = new ArchiveEntryPlanner(
            new NuspecDocumentWriter(),
            new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1, 2, 3] }),
            new PackagePathValidator());
        var plan = planner.Plan(package);
        Assert.Equal("NetWasm.Sample.nuspec", plan.Entries.Single(entry => entry.Path.EndsWith(".nuspec", StringComparison.Ordinal)).Path);
        Assert.Contains(plan.Entries, entry => entry.Path == "lib/NetWasm,Version=v0.1/Sample.dll");
    }

    [Fact]
    public void PlannerRejectsArchiveLimitBeforeReadingAnyPayload()
    {
        var reader = new RecordingReader([1, 2, 3]);
        var package = PackTestFixtures.Package("sample.nupkg") with
        {
            Determinism = DeterminismPolicy.Default with { MaxArchiveBytes = 5 }
        };

        var error = Assert.Throws<NetWasmPackException>(() => new ArchiveEntryPlanner(
            new NuspecDocumentWriter(), reader, new PackagePathValidator()).Plan(package));

        Assert.Equal(NetWasmPackErrorCode.NWPK014, error.Code);
        Assert.Empty(reader.Requests);
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 10, 0)]
    public void PlannerRejectsInvalidArchivePolicy(int maxEntries, long maxEntryBytes, long maxArchiveBytes)
    {
        var package = PackTestFixtures.Package("sample.nupkg") with
        {
            Determinism = DeterminismPolicy.Default with
            {
                MaxEntries = maxEntries,
                MaxEntryBytes = maxEntryBytes,
                MaxArchiveBytes = maxArchiveBytes
            }
        };

        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => new ArchiveEntryPlanner(
            new NuspecDocumentWriter(), new RecordingReader([1]), new PackagePathValidator()).Plan(package)).Code);
    }

    [Fact]
    public void PlannerRejectsWhenEnvelopeConsumesTheWholeArchiveBudget()
    {
        var package = PackTestFixtures.Package("sample.nupkg");
        var planner = new ArchiveEntryPlanner(
            new NuspecDocumentWriter(), new RecordingReader([1]), new PackagePathValidator());
        var envelopeBytes = planner.Plan(package).Entries.Take(3).Sum(entry => entry.Content.Length);
        var reader = new RecordingReader([1]);

        var limitedPlanner = new ArchiveEntryPlanner(
            new NuspecDocumentWriter(), reader, new PackagePathValidator());
        var error = Assert.Throws<NetWasmPackException>(() => limitedPlanner.Plan(package with
        {
            Determinism = DeterminismPolicy.Default with { MaxArchiveBytes = envelopeBytes }
        }));

        Assert.Equal(NetWasmPackErrorCode.NWPK014, error.Code);
        Assert.Empty(reader.Requests);
    }

    [Fact]
    public void ArchiveWriterIsDeterministicAndAtomicallyReplacesOutput()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var output = Path.Combine(directory.Path, "Sample.nupkg");
        var package = PackTestFixtures.Package(output);
        var planner = new ArchiveEntryPlanner(
            new NuspecDocumentWriter(),
            new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1, 2, 3] }),
            new PackagePathValidator());
        var writer = CreateWriter();
        var first = writer.Write(planner.Plan(package));
        var bytes = File.ReadAllBytes(output);
        File.WriteAllText(output, "stale output");
        var second = writer.Write(planner.Plan(package));
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(bytes, File.ReadAllBytes(output));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp", SearchOption.TopDirectoryOnly));
        using var archive = ZipFile.OpenRead(output);
        Assert.Contains(archive.Entries, entry => entry.FullName == "lib/NetWasm,Version=v0.1/Sample.dll");
    }

    [Theory]
    [InlineData(NetWasm.Sdk.Pack.Packing.CompressionMode.Store)]
    [InlineData(NetWasm.Sdk.Pack.Packing.CompressionMode.Fastest)]
    public void ArchiveWriterSupportsAllQualifiedCompressionModes(NetWasm.Sdk.Pack.Packing.CompressionMode compression)
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var package = PackTestFixtures.Package(Path.Combine(directory.Path, $"{compression}.nupkg"));
        var planner = new ArchiveEntryPlanner(
            new NuspecDocumentWriter(),
            new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1, 2, 3] }),
            new PackagePathValidator());
        var plan = planner.Plan(package) with { Policy = DeterminismPolicy.Default with { Compression = compression } };
        Assert.True(File.Exists(CreateWriter().Write(plan).Path));
    }

    [Fact]
    public void ArchiveWriterLeavesNoOutputOrTemporaryFileWhenInputFails()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var output = Path.Combine(directory.Path, "Sample.nupkg");
        var package = PackTestFixtures.Package(output);
        var planner = new ArchiveEntryPlanner(new NuspecDocumentWriter(), new LocalPackageFileReader(new LocalInputFileStreamOpener()), new PackagePathValidator());
        var writer = CreateWriter();
        var error = Assert.Throws<NetWasmPackException>(() => writer.Write(planner.Plan(package)));
        Assert.Equal(NetWasmPackErrorCode.NWPK011, error.Code);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ArchiveWriterRejectsPhysicalArchiveLargerThanByteBudget()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var output = Path.Combine(directory.Path, "budget.nupkg");
        var identity = new PackageIdentity("Sample", "1.0.0");
        var entries = ImmutableArray.Create(
            ArchiveEntry.FromBytes("[Content_Types].xml", [1]),
            ArchiveEntry.FromBytes("_rels/.rels", [2]),
            ArchiveEntry.FromBytes("Sample.nuspec", "<package/>"u8.ToArray()));
        var plan = new ArchivePlan(output, entries, DeterminismPolicy.Default with { MaxArchiveBytes = 12 }, identity);

        var error = Assert.Throws<NetWasmPackException>(() => CreateWriter().Write(plan));

        Assert.Equal(NetWasmPackErrorCode.NWPK014, error.Code);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void PlannerAndValidatorRejectCollisionsAndIncompleteEnvelope()
    {
        var package = PackTestFixtures.Package("/tmp/sample.nupkg", [new CanonicalPackageFile("sample.dll", "[Content_Types].xml")]);
        var planner = new ArchiveEntryPlanner(
            new NuspecDocumentWriter(),
            new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1] }),
            new PackagePathValidator());
        Assert.Equal(NetWasmPackErrorCode.NWPK008, Assert.Throws<NetWasmPackException>(() => planner.Plan(package)).Code);
        var plan = new ArchivePlan("/tmp/sample.nupkg", [ArchiveEntry.FromBytes("only.txt", [1])], DeterminismPolicy.Default, package.Identity);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => new PackageValidator(new PackagePathValidator()).Validate(plan)).Code);
    }

    [Fact]
    public void ArchiveWriterRejectsInvalidPolicyAndCollaborators()
    {
        var package = PackTestFixtures.Package("/tmp/sample.nupkg");
        var plan = new ArchivePlan("/tmp/sample.nupkg", [
            ArchiveEntry.FromBytes("[Content_Types].xml", [1]),
            ArchiveEntry.FromBytes("_rels/.rels", [1]),
            ArchiveEntry.FromBytes("NetWasm.Sample.nuspec", Encoding.UTF8.GetBytes("<package/>"))
        ], DeterminismPolicy.Default, package.Identity);
        var invalid = plan with { Policy = DeterminismPolicy.Default with { Compression = (NetWasm.Sdk.Pack.Packing.CompressionMode)99 } };
        var writer = CreateWriter();
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => writer.Write(invalid)).Code);
        Assert.Throws<ArgumentNullException>(() => new DeterministicArchiveWriter(null!, new PackageArchiveOutputValidator(new PackagePathValidator())));
        Assert.Throws<ArgumentNullException>(() => new DeterministicArchiveWriter(new PackageValidator(new PackagePathValidator()), null!));
        Assert.Throws<ArgumentNullException>(() => new ArchiveEntryPlanner(null!, new LocalPackageFileReader(new LocalInputFileStreamOpener()), new PackagePathValidator()));
        Assert.Throws<ArgumentNullException>(() => new ArchiveEntryPlanner(new NuspecDocumentWriter(), null!, new PackagePathValidator()));
        Assert.Throws<ArgumentNullException>(() => new ArchiveEntryPlanner(new NuspecDocumentWriter(), new LocalPackageFileReader(new LocalInputFileStreamOpener()), null!));
        Assert.Throws<ArgumentNullException>(() => new LocalPackageFileReader(null!));
        Assert.Throws<ArgumentNullException>(() => new PackageValidator(null!));
        Assert.Throws<ArgumentNullException>(() => new PackageArchiveOutputValidator(null!));
        Assert.Throws<ArgumentNullException>(() => new InMemoryPackageFileReader(null!));
    }

    [Fact]
    public void ValidatorChecksPolicyEntryAndIdentityFailures()
    {
        var package = PackTestFixtures.Package("sample.nupkg");
        var envelope = new[]
        {
            ArchiveEntry.FromBytes("[Content_Types].xml", [1]),
            ArchiveEntry.FromBytes("_rels/.rels", [1]),
            ArchiveEntry.FromBytes("NetWasm.Sample.nuspec", Encoding.UTF8.GetBytes("<package />"))
        }.ToImmutableArray();
        var validator = new PackageValidator(new PackagePathValidator());
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan("sample.nupkg", envelope, DeterminismPolicy.Default with { Utf8Names = false }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan("sample.nupkg", envelope.Add(new ArchiveEntry("entry", default)), DeterminismPolicy.Default, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan("sample.nupkg", [
            ArchiveEntry.FromBytes("[Content_Types].xml", [1]),
            ArchiveEntry.FromBytes("_rels/.rels", [1]),
            ArchiveEntry.FromBytes("NetWasm.Sample.nuspec", Encoding.UTF8.GetBytes("NetWasm0.1"))
        ], DeterminismPolicy.Default, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan("sample.nupkg", [
            ArchiveEntry.FromBytes("[Content_Types].xml", [1]),
            ArchiveEntry.FromBytes("_rels/.rels", [1]),
            ArchiveEntry.FromBytes("NetWasm.Sample.nuspec", Encoding.UTF8.GetBytes("<package><dependencies><group targetFramework=\"NetWasm0.1\" /></dependencies></package>"))
        ], DeterminismPolicy.Default, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan("sample.nupkg", [
            ArchiveEntry.FromBytes("[Content_Types].xml", [1]),
            ArchiveEntry.FromBytes("_rels/.rels", [1]),
            ArchiveEntry.FromBytes("NetWasm.Sample.nuspec", Encoding.UTF8.GetBytes("<package>"))
        ], DeterminismPolicy.Default, package.Identity))).Code);
    }

    [Fact]
    public void PackageValidatorCoversEmptyPolicySizeAndNormalizedPathFailures()
    {
        var package = PackTestFixtures.Package("sample.nupkg");
        var validator = new PackageValidator(new PackagePathValidator());
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => validator.Validate(
            new ArchivePlan("sample.nupkg", ImmutableArray<ArchiveEntry>.Empty, DeterminismPolicy.Default, package.Identity))).Code);
        var envelope = ImmutableArray.Create(
            ArchiveEntry.FromBytes("[Content_Types].xml", [1]),
            ArchiveEntry.FromBytes("_rels/.rels", [1]),
            ArchiveEntry.FromBytes("NetWasm.Sample.nuspec", Encoding.UTF8.GetBytes("<package />")));
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope.Add(ArchiveEntry.FromBytes("large", [1, 2])), DeterminismPolicy.Default with { MaxEntryBytes = 1 }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK008, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope.Add(ArchiveEntry.FromBytes("lib\\a.dll", [1])), DeterminismPolicy.Default, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, null!, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK008, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope.Add(ArchiveEntry.FromBytes("only.txt", [1])).Add(ArchiveEntry.FromBytes("only.txt", [2])), DeterminismPolicy.Default, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { EntryTimestamp = new DateTimeOffset(1979, 1, 1, 0, 0, 0, TimeSpan.Zero) }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { EntryTimestamp = new DateTimeOffset(2108, 1, 1, 0, 0, 0, TimeSpan.Zero) }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { EntryTimestamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, 500, TimeSpan.Zero) }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { EntryTimestamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)) }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { CompressionLevel = -1 }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { CompressionLevel = 10 }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { MaxEntries = 2 }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { MaxEntries = 0 }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { MaxEntryBytes = 0 }, package.Identity))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(new ArchivePlan(
            "sample.nupkg", envelope, DeterminismPolicy.Default with { MaxArchiveBytes = 0 }, package.Identity))).Code);
    }

    [Fact]
    public void PlannerWrapsGenericReaderErrorsAndReaderMapsFilesystemErrors()
    {
        var package = PackTestFixtures.Package("sample.nupkg");
        var throwing = new ArchiveEntryPlanner(new NuspecDocumentWriter(), new ThrowingReader(), new PackagePathValidator());
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => throwing.Plan(package)).Code);
        using var directory = new PackTestFixtures.TemporaryDirectory();
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => new LocalPackageFileReader(new LocalInputFileStreamOpener()).Read(directory.Path, 1024)).Code);
    }

    [Fact]
    public void PlannerRejectsEvaluatedInputHashChanges()
    {
        var package = PackTestFixtures.Package("sample.nupkg") with
        {
            Files = [new CanonicalPackageFile("sample.dll", "lib/NetWasm,Version=v0.1/Sample.dll") { ExpectedSha256 = "00" }]
        };
        var planner = new ArchiveEntryPlanner(
            new NuspecDocumentWriter(),
            new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1] }),
            new PackagePathValidator());
        Assert.Equal(NetWasmPackErrorCode.NWPK010, Assert.Throws<NetWasmPackException>(() => planner.Plan(package)).Code);
        var lengthPackage = PackTestFixtures.Package("sample.nupkg") with
        {
            Files = [new CanonicalPackageFile("sample.dll", "lib/NetWasm,Version=v0.1/Sample.dll") { ExpectedLength = 2 }]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK010, Assert.Throws<NetWasmPackException>(() => planner.Plan(lengthPackage)).Code);
    }

    [Fact]
    public void OutputValidatorRejectsUnreadableOrMismatchedArchives()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var invalid = Path.Combine(directory.Path, "invalid.nupkg");
        File.WriteAllText(invalid, "not zip");
        var package = PackTestFixtures.Package(invalid);
        var plan = new ArchiveEntryPlanner(
            new NuspecDocumentWriter(),
            new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1] }),
            new PackagePathValidator()).Plan(package);
        var validator = new PackageArchiveOutputValidator(new PackagePathValidator());
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => validator.Validate(invalid, plan)).Code);
        var archive = CreateWriter().Write(plan);
        var changed = plan with
        {
            Entries = plan.Entries.Select(entry => entry.Path.EndsWith(".dll", StringComparison.Ordinal)
                ? ArchiveEntry.FromBytes(entry.Path, [9])
                : entry).ToImmutableArray()
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => validator.Validate(archive.Path, changed)).Code);
    }

    [Fact]
    public void OutputValidatorRejectsUnexpectedEntryCountAndPath()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var path = Path.Combine(directory.Path, "wrong.nupkg");
        var package = PackTestFixtures.Package(path);
        var plan = new ArchiveEntryPlanner(new NuspecDocumentWriter(), new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1] }), new PackagePathValidator()).Plan(package);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("wrong").Open().Dispose();
        }
        var validator = new PackageArchiveOutputValidator(new PackagePathValidator());
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => validator.Validate(path, plan)).Code);
        File.Delete(path);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var entry in plan.Entries)
            {
                archive.CreateEntry("wrong/" + entry.Path).Open().Dispose();
            }
        }
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => validator.Validate(path, plan)).Code);
        Assert.Throws<ArgumentException>(() => validator.Validate(" ", plan));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(path, null!));
        var validPath = CreateWriter().Write(plan).Path;
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => validator.Validate(validPath, plan with { Policy = plan.Policy with { MaxEntryBytes = 0 } })).Code);
    }

    [Fact]
    public void OutputValidatorRejectsAnEntryThatExceedsItsBoundedReadPolicy()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var path = Path.Combine(directory.Path, "large.nupkg");
        var package = PackTestFixtures.Package(path);
        var entries = new[]
        {
            ArchiveEntry.FromBytes("[Content_Types].xml", [1]),
            ArchiveEntry.FromBytes("_rels/.rels", [1]),
            ArchiveEntry.FromBytes("NetWasm.Sample.nuspec", [1]),
            ArchiveEntry.FromBytes("large.bin", [1, 2])
        }.ToImmutableArray();
        var plan = new ArchivePlan(path, entries, DeterminismPolicy.Default with { MaxEntryBytes = 1 }, package.Identity);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var entry in entries)
            {
                using var stream = archive.CreateEntry(entry.Path).Open();
                stream.Write(entry.Content.AsSpan());
            }
        }

        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() =>
            new PackageArchiveOutputValidator(new PackagePathValidator()).Validate(path, plan)).Code);
    }

    [Fact]
    public void OutputValidatorEnforcesCumulativeArchiveReadPolicy()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var path = Path.Combine(directory.Path, "cumulative.nupkg");
        var package = PackTestFixtures.Package(path);
        var entries = ImmutableArray.Create(
            ArchiveEntry.FromBytes("[Content_Types].xml", [1]),
            ArchiveEntry.FromBytes("_rels/.rels", [2]),
            ArchiveEntry.FromBytes("NetWasm.Sample.nuspec", [3]),
            ArchiveEntry.FromBytes("payload.bin", [4]));
        var plan = new ArchivePlan(path, entries, DeterminismPolicy.Default with { MaxEntryBytes = 1, MaxArchiveBytes = 3 }, package.Identity);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var entry in entries)
            {
                using var stream = archive.CreateEntry(entry.Path).Open();
                stream.Write(entry.Content.AsSpan());
            }
        }

        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() =>
            new PackageArchiveOutputValidator(new PackagePathValidator()).Validate(path, plan)).Code);
    }

    [Fact]
    public void ArchiveWriterRejectsOutputDirectoryAndArchiveSizeFailures()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var blockedParent = Path.Combine(directory.Path, "parent");
        File.WriteAllBytes(blockedParent, [1]);
        var package = PackTestFixtures.Package(Path.Combine(blockedParent, "sample.nupkg"));
        var planner = new ArchiveEntryPlanner(new NuspecDocumentWriter(), new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1] }), new PackagePathValidator());
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => CreateWriter().Write(planner.Plan(package))).Code);
        var tinyPlan = planner.Plan(PackTestFixtures.Package(Path.Combine(directory.Path, "tiny.nupkg"))) with
        {
            Policy = DeterminismPolicy.Default with { MaxArchiveBytes = 1 }
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => CreateWriter().Write(tinyPlan)).Code);
        var throwingOutput = new DeterministicArchiveWriter(new PackageValidator(new PackagePathValidator()), new ThrowingOutputValidator());
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => throwingOutput.Write(tinyPlan with { Policy = DeterminismPolicy.Default })).Code);
    }

    private sealed class ThrowingOutputValidator : IArchiveOutputValidator
    {
        public void Validate(string archivePath, ArchivePlan plan) => throw new IOException("reopen");
    }

    private sealed class ThrowingReader : IPackageFileReader
    {
        public byte[] Read(string sourcePath, long maxLength) => throw new IOException("input");
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

    [Fact]
    public void LocalAndInMemoryReadersCopyBytesAndRejectMissingInput()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var path = Path.Combine(directory.Path, "a.dll");
        File.WriteAllBytes(path, [1, 2]);
        Assert.Equal([1, 2], new LocalPackageFileReader(new LocalInputFileStreamOpener()).Read(path, 1024));
        Assert.Equal([3], new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["a"] = [3] }).Read("a", 1024));
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => new LocalPackageFileReader(new LocalInputFileStreamOpener()).Read(" ", 1024)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => new InMemoryPackageFileReader(new Dictionary<string, byte[]>()).Read("a", 1024)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["a"] = [1, 2] }).Read("a", 1)).Code);
    }

    [Fact]
    public void ReadersRejectInvalidSizePoliciesAndOversizedLocalFiles()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var path = Path.Combine(directory.Path, "a.dll");
        File.WriteAllBytes(path, [1, 2]);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => new LocalPackageFileReader(new LocalInputFileStreamOpener()).Read(path, 0)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => new LocalPackageFileReader(new LocalInputFileStreamOpener()).Read(path, 1)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => new InMemoryPackageFileReader(new Dictionary<string, byte[]>()).Read("a", 0)).Code);
    }

    [Fact]
    public void LocalReaderRejectsFilesThatGrowDuringStreaming()
    {
        var opener = new MutatingStreamOpener([1, 2]);
        var reader = new LocalPackageFileReader(opener);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => reader.Read("input", 1)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK010, Assert.Throws<NetWasmPackException>(() => reader.Read("input", 10)).Code);
    }

    private static DeterministicArchiveWriter CreateWriter()
    {
        var pathValidator = new PackagePathValidator();
        return new DeterministicArchiveWriter(new PackageValidator(pathValidator), new PackageArchiveOutputValidator(pathValidator));
    }

    private sealed class MutatingStreamOpener(byte[] bytes) : IInputFileStreamOpener
    {
        public Stream Open(string sourcePath) => new MutatingStream(bytes);
    }

    private sealed class MutatingStream(byte[] bytes) : MemoryStream(bytes)
    {
        private bool hasRead;

        public override long Length => hasRead ? base.Length : base.Length - 1;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            hasRead = true;
            return read;
        }
    }
}
