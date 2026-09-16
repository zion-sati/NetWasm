using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class PackageArchiveCanonicalizerTests
{
    [Fact]
    public void CanonicalizerProducesStableSortedArchiveAndCoreProperties()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var input = Path.Combine(directory.Path, "input.nupkg");
        var firstPath = Path.Combine(directory.Path, "first.nupkg");
        var secondPath = Path.Combine(directory.Path, "second.nupkg");
        WriteStockLikeArchive(input, corePropertiesName: "random-one.psmdcp", relationshipId: "RANDOM-ONE");

        var canonicalizer = CreateCanonicalizer();
        var policy = DeterminismPolicy.Default with
        {
            EntryTimestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)
        };
        var identity = new PackageIdentity("NetWasm.Sample", "0.2.0");
        var first = canonicalizer.Canonicalize(new PackageArchiveNormalizationRequest(input, firstPath, identity, policy));

        WriteStockLikeArchive(input, corePropertiesName: "random-two.psmdcp", relationshipId: "RANDOM-TWO");
        var second = canonicalizer.Canonicalize(new PackageArchiveNormalizationRequest(input, secondPath, identity, policy));

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
        using var archive = ZipFile.OpenRead(firstPath);
        var names = archive.Entries.Select(static entry => entry.FullName).ToArray();
        Assert.Equal(names.OrderBy(static name => name, StringComparer.Ordinal), names);
        Assert.Contains(archive.Entries, entry => entry.FullName == "package/services/metadata/core-properties/core-properties.psmdcp");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith(".psmdcp", StringComparison.Ordinal) &&
            entry.FullName != "package/services/metadata/core-properties/core-properties.psmdcp");

        var core = Encoding.UTF8.GetString(ReadEntry(archive, "package/services/metadata/core-properties/core-properties.psmdcp"));
        Assert.Contains("2023-11-14T22:13:20Z", core, StringComparison.Ordinal);
        var relationships = Encoding.UTF8.GetString(ReadEntry(archive, "_rels/.rels"));
        Assert.DoesNotContain("RANDOM-", relationships, StringComparison.Ordinal);
        Assert.Contains("core-properties.psmdcp", relationships, StringComparison.Ordinal);
        Assert.Contains(archive.Entries, entry => entry.FullName == "symbols/NetWasm.Sample.pdb");
        Assert.Equal([7, 8, 9], ReadEntry(archive, "symbols/NetWasm.Sample.pdb"));
    }

    [Fact]
    public void CanonicalizerPreservesArchiveIdentityAndInputMetadata()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var input = Path.Combine(directory.Path, "input.nupkg");
        var output = Path.Combine(directory.Path, "output.nupkg");
        WriteStockLikeArchive(input, corePropertiesName: "random.psmdcp", relationshipId: "RANDOM");

        var result = CreateCanonicalizer().Canonicalize(new PackageArchiveNormalizationRequest(
            input,
            output,
            new PackageIdentity("NetWasm.Sample", "0.2.0"),
            DeterminismPolicy.Default));

        Assert.Equal(output, result.Path);
        Assert.Equal("NetWasm.Sample", result.Identity.Id);
        Assert.Equal("0.2.0", result.Identity.Version);
        using var archive = ZipFile.OpenRead(output);
        Assert.Equal([1, 2, 3], ReadEntry(archive, "lib/NetWasm,Version=v0.1/Sample.dll"));
        Assert.Equal([7, 8, 9], ReadEntry(archive, "symbols/NetWasm.Sample.pdb"));
        Assert.Equal("README", Encoding.UTF8.GetString(ReadEntry(archive, "README.md")));
    }

    [Fact]
    public void ArchiveTaskPassesFixedEpochToCanonicalizer()
    {
        var canonicalizer = new RecordingCanonicalizer();
        var task = new DeterministicPackageArchiveTask(canonicalizer)
        {
            BuildEngine = new PackTestFixtures.RecordingBuildEngine(),
            PackagePath = "/tmp/NetWasm.Sample.0.1.0.nupkg",
            PackageId = "NetWasm.Sample",
            PackageVersion = "0.2.0",
            SourceDateEpoch = "1700000000"
        };

        Assert.True(task.Execute());
        Assert.NotNull(canonicalizer.Request);
        Assert.Equal("/tmp/NetWasm.Sample.0.1.0.nupkg", canonicalizer.Request.InputPath);
        Assert.Equal(new PackageIdentity("NetWasm.Sample", "0.2.0"), canonicalizer.Request.Identity);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), canonicalizer.Request.Policy.EntryTimestamp);
    }

    [Fact]
    public void ArchiveTaskUsesDefaultEpochWhenSourceEpochIsOmitted()
    {
        var canonicalizer = new RecordingCanonicalizer();
        var task = new DeterministicPackageArchiveTask(canonicalizer)
        {
            BuildEngine = new PackTestFixtures.RecordingBuildEngine(),
            PackagePath = "/tmp/NetWasm.Sample.0.1.0.nupkg",
            PackageId = "NetWasm.Sample",
            PackageVersion = "0.2.0"
        };

        Assert.True(task.Execute());
        Assert.Equal(DeterminismPolicy.Default.EntryTimestamp, canonicalizer.Request!.Policy.EntryTimestamp);
    }

    [Fact]
    public void NormalizerHandlesUnchangedMetadataDuplicateRelationshipsAndMalformedInput()
    {
        var timestamp = DeterminismPolicy.Default.EntryTimestamp;
        var corePath = "package/services/metadata/core-properties/random.psmdcp";
        var core = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><core><created>1980-01-01T00:00:00Z</created><modified>1980-01-01T00:00:00Z</modified></core>");
        var relationships = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><Relationships><Relationship Type=\"type\" Target=\"/" + corePath + "\" /><Relationship Type=\"type\" Target=\"/" + corePath + "\" /><Relationship Type=\"other\" /><Relationship /></Relationships>");
        var contentTypes = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><Types><Override PartName=\"/" + corePath + "\" ContentType=\"application/xml\" /><Override PartName=\"/other\" ContentType=\"application/octet-stream\" /></Types>");
        var archive = new PackageArchive(
            new PackageIdentity("Sample", "1.0.0"),
            [
                ArchiveEntry.FromBytes("README.md", [1]),
                ArchiveEntry.FromBytes(corePath, core),
                ArchiveEntry.FromBytes("_rels/.rels", relationships),
                ArchiveEntry.FromBytes("[Content_Types].xml", contentTypes),
                ArchiveEntry.FromBytes("package/services/metadata/core-properties/not-a-core.txt", [2]),
                ArchiveEntry.FromBytes("package/services/metadata/core-properties/.psmdcp", [3])
            ],
            DeterminismPolicy.Default);

        var normalized = new CorePropertiesNormalizer().Normalize(archive);
        Assert.Contains(normalized.Entries, entry => entry.Path == "package/services/metadata/core-properties/core-properties.psmdcp");
        Assert.Equal(core, normalized.Entries.Single(entry => entry.Path.EndsWith("core-properties.psmdcp", StringComparison.Ordinal)).Content);
        var normalizedRelationships = Encoding.UTF8.GetString(normalized.Entries.Single(entry => entry.Path == "_rels/.rels").Content.AsSpan());
        Assert.Contains("1", normalizedRelationships, StringComparison.Ordinal);
        Assert.True(normalizedRelationships.IndexOf("Type=\"other\"", StringComparison.Ordinal) <
            normalizedRelationships.IndexOf("Type=\"type\"", StringComparison.Ordinal));
        var normalizedContentTypes = Encoding.UTF8.GetString(normalized.Entries.Single(entry => entry.Path == "[Content_Types].xml").Content.AsSpan());
        Assert.Contains("PartName=\"/package/services/metadata/core-properties/core-properties.psmdcp\"", normalizedContentTypes, StringComparison.Ordinal);
        Assert.Contains("PartName=\"/other\"", normalizedContentTypes, StringComparison.Ordinal);
        var normalizedAgain = new CorePropertiesNormalizer().Normalize(normalized);
        Assert.Equal(normalized.Entries.Select(static entry => entry.Path), normalizedAgain.Entries.Select(static entry => entry.Path));
        Assert.True(normalized.Entries.Single(entry => entry.Path == "_rels/.rels").Content
            .SequenceEqual(normalizedAgain.Entries.Single(entry => entry.Path == "_rels/.rels").Content));

        var noCore = new PackageArchive(
            archive.Identity,
            [
                ArchiveEntry.FromBytes("README.md", [1]),
                ArchiveEntry.FromBytes("_rels/.rels", Encoding.UTF8.GetBytes("<Relationships><Relationship Type=\"type\" Target=\"/target\" Id=\"R01\" /></Relationships>"))
            ],
            archive.Policy);
        var noCoreResult = new CorePropertiesNormalizer().Normalize(noCore);
        Assert.Same(noCore.Entries[0], noCoreResult.Entries[0]);
        Assert.NotSame(noCore.Entries[1], noCoreResult.Entries[1]);

        var duplicate = archive with
        {
            Entries = archive.Entries.Add(ArchiveEntry.FromBytes("package/services/metadata/core-properties/second.psmdcp", [4]))
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK008, Assert.Throws<NetWasmPackException>(() => new CorePropertiesNormalizer().Normalize(duplicate)).Code);

        var malformedCore = archive with { Entries = [ArchiveEntry.FromBytes(corePath, Encoding.UTF8.GetBytes("<broken"))] };
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => new CorePropertiesNormalizer().Normalize(malformedCore)).Code);
        var malformedRelationships = noCore with { Entries = [ArchiveEntry.FromBytes("_rels/.rels", Encoding.UTF8.GetBytes("<broken"))] };
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => new CorePropertiesNormalizer().Normalize(malformedRelationships)).Code);

        var emptyRelationships = noCore with
        {
            Entries = [ArchiveEntry.FromBytes("_rels/.rels", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>"))]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => new CorePropertiesNormalizer().Normalize(emptyRelationships)).Code);

        var canonicalRelationshipId = "R" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("type\nExternal\n/target")))[..16];
        var unchangedRelationships = noCore with
        {
            Entries = [ArchiveEntry.FromBytes("_rels/.rels", Encoding.UTF8.GetBytes($"<Relationships><Relationship Type=\"type\" Target=\"/target\" Id=\"{canonicalRelationshipId}\" TargetMode=\"External\" /></Relationships>"))]
        };
        var unchanged = new CorePropertiesNormalizer().Normalize(unchangedRelationships);
        Assert.Same(unchangedRelationships.Entries[0], unchanged.Entries[0]);

        var malformedContentTypes = archive with
        {
            Entries = [
                ArchiveEntry.FromBytes(corePath, core),
                ArchiveEntry.FromBytes("[Content_Types].xml", Encoding.UTF8.GetBytes("<broken"))
            ]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => new CorePropertiesNormalizer().Normalize(malformedContentTypes)).Code);

        var boundedOutputReader = typeof(PackageArchiveOutputValidator).GetMethod("ReadBounded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var outputOverflow = Assert.Throws<System.Reflection.TargetInvocationException>(() => boundedOutputReader.Invoke(null, [new MemoryStream([1, 2]), 1L]));
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.IsType<NetWasmPackException>(outputOverflow.InnerException).Code);
        var outputInvalid = Assert.Throws<System.Reflection.TargetInvocationException>(() => boundedOutputReader.Invoke(null, [new MemoryStream(), -1L]));
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.IsType<NetWasmPackException>(outputInvalid.InnerException).Code);

        var boundedArchiveReader = typeof(PackageArchiveReader).GetMethod("ReadBounded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var archiveOverflow = Assert.Throws<System.Reflection.TargetInvocationException>(() => boundedArchiveReader.Invoke(null, [new MemoryStream([1, 2]), 1L]));
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.IsType<NetWasmPackException>(archiveOverflow.InnerException).Code);
        var archiveInvalid = Assert.Throws<System.Reflection.TargetInvocationException>(() => boundedArchiveReader.Invoke(null, [new MemoryStream(), -1L]));
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.IsType<NetWasmPackException>(archiveInvalid.InnerException).Code);
    }

    [Fact]
    public void ReaderRejectsIncompleteDuplicateOversizedAndInvalidArchives()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var reader = new PackageArchiveReader(new PackagePathValidator());
        var identity = new PackageIdentity("Sample", "1.0.0");
        var request = (string path, DeterminismPolicy policy) => new PackageArchiveNormalizationRequest(path, path, identity, policy);
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => reader.Read(request(" ", DeterminismPolicy.Default))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => reader.Read(request(Path.Combine(directory.Path, "missing.nupkg"), DeterminismPolicy.Default))).Code);

        var invalid = Path.Combine(directory.Path, "invalid.nupkg");
        File.WriteAllText(invalid, "not an archive");
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => reader.Read(request(invalid, DeterminismPolicy.Default))).Code);

        var empty = Path.Combine(directory.Path, "empty.nupkg");
        WriteZip(empty, []);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => reader.Read(request(empty, DeterminismPolicy.Default))).Code);

        var duplicate = Path.Combine(directory.Path, "duplicate.nupkg");
        WriteZip(duplicate, [("entry", [1]), ("entry", [2])]);
        Assert.Equal(NetWasmPackErrorCode.NWPK008, Assert.Throws<NetWasmPackException>(() => reader.Read(request(duplicate, DeterminismPolicy.Default))).Code);

        var unsafePath = Path.Combine(directory.Path, "unsafe.nupkg");
        WriteZip(unsafePath, [("../entry", [1])]);
        Assert.Equal(NetWasmPackErrorCode.NWPK009, Assert.Throws<NetWasmPackException>(() => reader.Read(request(unsafePath, DeterminismPolicy.Default))).Code);

        var oversized = Path.Combine(directory.Path, "oversized.nupkg");
        WriteZip(oversized, [("entry", [1, 2])]);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => reader.Read(request(oversized, DeterminismPolicy.Default with { MaxEntryBytes = 1 }))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => reader.Read(request(oversized, DeterminismPolicy.Default with { MaxEntryBytes = 0 }))).Code);
        var cumulative = Path.Combine(directory.Path, "cumulative.nupkg");
        WriteZip(cumulative, [("first", [1, 2]), ("second", [3, 4])]);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => reader.Read(request(
            cumulative,
            DeterminismPolicy.Default with { MaxEntryBytes = 2, MaxArchiveBytes = 3 }))).Code);
        var signed = Path.Combine(directory.Path, "signed.nupkg");
        WriteZip(signed, [("package/services/metadata/signatures/signature.p7s", [1]), ("entry", [2])]);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => reader.Read(request(signed, DeterminismPolicy.Default))).Code);
        foreach (var signaturePath in new[]
        {
            ".signature.p7s",
            "nested/.signature.p7s"
        })
        {
            var signatureVariant = Path.Combine(directory.Path, signaturePath.Replace('/', '_'));
            WriteZip(signatureVariant, [(signaturePath, [1]), ("entry", [2])]);
            Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => reader.Read(request(signatureVariant, DeterminismPolicy.Default))).Code);
        }
        var nonSignatureMetadata = Path.Combine(directory.Path, "metadata-signature.txt.nupkg");
        WriteZip(nonSignatureMetadata, [("package/services/metadata/signatures/signature.txt", [1]), ("entry", [2])]);
        Assert.Equal(2, reader.Read(request(nonSignatureMetadata, DeterminismPolicy.Default)).Entries.Length);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => reader.Read(request(oversized, null!))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => reader.Read(new PackageArchiveNormalizationRequest(oversized, oversized, null!, DeterminismPolicy.Default))).Code);

        var tooMany = Path.Combine(directory.Path, "too-many.nupkg");
        WriteZip(tooMany, [("one", [1]), ("two", [2])]);
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => reader.Read(request(tooMany, DeterminismPolicy.Default with { MaxEntries = 1 }))).Code);
    }

    [Fact]
    public void CanonicalizerAndTaskRejectMissingCollaboratorsAndSanitizeFailures()
    {
        var pathValidator = new PackagePathValidator();
        var writer = new DeterministicArchiveWriter(
            new PackageValidator(pathValidator),
            new PackageArchiveOutputValidator(pathValidator));
        Assert.Throws<ArgumentNullException>(() => new PackageArchiveCanonicalizer(null!, new CorePropertiesNormalizer(), writer));
        Assert.Throws<ArgumentNullException>(() => new PackageArchiveCanonicalizer(new PackageArchiveReader(pathValidator), null!, writer));
        Assert.Throws<ArgumentNullException>(() => new PackageArchiveCanonicalizer(new PackageArchiveReader(pathValidator), new CorePropertiesNormalizer(), null!));
        Assert.Throws<ArgumentNullException>(() => new PackageArchiveCanonicalizer(new PackageArchiveReader(pathValidator), new CorePropertiesNormalizer(), writer).Canonicalize(null!));
        Assert.Throws<ArgumentNullException>(() => new PackageArchiveReader(null!));
        Assert.Throws<ArgumentNullException>(() => new DeterministicPackageArchiveTask(null!));
        Assert.NotNull(new DeterministicPackageArchiveTask());

        var rangeEngine = new PackTestFixtures.RecordingBuildEngine();
        var rangeTask = new DeterministicPackageArchiveTask(new ThrowingCanonicalizer(new InvalidOperationException("private path")))
        {
            BuildEngine = rangeEngine,
            PackagePath = "/tmp/sample.nupkg",
            PackageId = "Sample",
            PackageVersion = "1.0.0",
            SourceDateEpoch = "9223372036854775807"
        };
        Assert.False(rangeTask.Execute());
        Assert.Contains(rangeEngine.Errors, error => error.Contains("NWPK013", StringComparison.Ordinal));

        var genericEngine = new PackTestFixtures.RecordingBuildEngine();
        var genericTask = new DeterministicPackageArchiveTask(new ThrowingCanonicalizer(new InvalidOperationException("private path")))
        {
            BuildEngine = genericEngine,
            PackagePath = "/tmp/sample.nupkg",
            PackageId = "Sample",
            PackageVersion = "1.0.0",
            SourceDateEpoch = "1700000000"
        };
        Assert.False(genericTask.Execute());
        Assert.Contains(genericEngine.Errors, error => error.Contains("NWPK014", StringComparison.Ordinal));

        var malformedEngine = new PackTestFixtures.RecordingBuildEngine();
        var malformedTask = new DeterministicPackageArchiveTask(new RecordingCanonicalizer())
        {
            BuildEngine = malformedEngine,
            PackagePath = "/tmp/sample.nupkg",
            PackageId = "Sample",
            PackageVersion = "1.0.0",
            SourceDateEpoch = "not-a-number"
        };
        Assert.False(malformedTask.Execute());
        Assert.Contains(malformedEngine.Errors, error => error.Contains("NWPK013", StringComparison.Ordinal));
    }

    private static PackageArchiveCanonicalizer CreateCanonicalizer()
    {
        var pathValidator = new PackagePathValidator();
        return new PackageArchiveCanonicalizer(
            new PackageArchiveReader(pathValidator),
            new CorePropertiesNormalizer(),
            new DeterministicArchiveWriter(
                new PackageValidator(pathValidator),
                new PackageArchiveOutputValidator(pathValidator)));
    }

    private static void WriteStockLikeArchive(string path, string corePropertiesName, string relationshipId)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["symbols/NetWasm.Sample.pdb"] = [7, 8, 9],
            ["NetWasm.Sample.nuspec"] = Encoding.UTF8.GetBytes("\uFEFF<package />"),
            ["package/services/metadata/core-properties/" + corePropertiesName] = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dcterms=\"http://purl.org/dc/terms/\"><dcterms:created>2000-01-01T00:00:00Z</dcterms:created><dcterms:modified>2000-01-01T00:00:00Z</dcterms:modified></cp:coreProperties>"),
            ["_rels/.rels"] = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Type=\"http://schemas.microsoft.com/packaging/2010/07/manifest\" Target=\"/NetWasm.Sample.nuspec\" Id=\"R-MANIFEST\" /><Relationship Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"/package/services/metadata/core-properties/" + corePropertiesName + "\" Id=\"" + relationshipId + "\" /></Relationships>"),
            ["[Content_Types].xml"] = Encoding.UTF8.GetBytes("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />"),
            ["README.md"] = Encoding.UTF8.GetBytes("README"),
            ["lib/NetWasm,Version=v0.1/Sample.dll"] = [1, 2, 3]
        };

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries.Reverse())
        {
            using var stream = archive.CreateEntry(entryPath).Open();
            stream.Write(content.AsSpan());
        }
    }

    private static byte[] ReadEntry(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void WriteZip(string path, IEnumerable<(string Path, byte[] Content)> entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            using var stream = archive.CreateEntry(entryPath).Open();
            stream.Write(content.AsSpan());
        }
    }

    private sealed class RecordingCanonicalizer : IPackageArchiveCanonicalizer
    {
        public PackageArchiveNormalizationRequest? Request { get; private set; }

        public PackageOutput Canonicalize(PackageArchiveNormalizationRequest request)
        {
            Request = request;
            return new PackageOutput(request.OutputPath, "hash", request.Identity);
        }
    }

    private sealed class ThrowingCanonicalizer(Exception exception) : IPackageArchiveCanonicalizer
    {
        public PackageOutput Canonicalize(PackageArchiveNormalizationRequest request) => throw exception;
    }
}
