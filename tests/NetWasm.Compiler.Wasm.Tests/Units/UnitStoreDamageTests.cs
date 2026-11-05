using System.Buffers.Binary;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// A store another process replaced, truncated or damaged costs reuse, never the compile:
// segments and manifests that are not what they claim read as no units, and a reference
// out of a segment's bounds is refused.
public sealed class UnitStoreDamageTests
{
    private static CompiledUnit Unit(string identity) =>
        new(identity, new(1, 2), [new(UnitQueryKind.StringLayout, "hello", new(3, 4))], [0x00, 0x0B], [], 1)
        {
            FunctionName = identity,
        };

    private static (byte[] Segment, UnitStoreChange Change) Committed(params string[] identities)
    {
        var writer = new UnitSegmentWriter();
        foreach (var identity in identities) Assert.True(writer.Add(Unit(identity)));
        var entries = writer.Entries.Select(entry => new UnitManifestEntry(entry.Hash, 0, entry.Offset)).ToArray();
        var change = new UnitStoreChange(1, writer,
            UnitManifestFormat.Write([new(1, writer.Count, writer.Length)], entries, 2), new HashSet<int> { 1 }, "summary");
        var memory = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(memory).Commit("app", change);
        return (memory.Segment("app", 1)!, change);
    }

    private static byte[] Pinned(byte[] bytes)
    {
        var pinned = GC.AllocateUninitializedArray<byte>(bytes.Length, pinned: true);
        bytes.CopyTo(pinned, 0);
        return pinned;
    }

    // Each header field a segment is checked by, set to a value no segment has.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(8, 0)]
    [InlineData(12, -1)]
    [InlineData(16, 0)]
    [InlineData(24, -1)]
    [InlineData(32, 0)]
    [InlineData(40, -1)]
    [InlineData(48, 0)]
    [InlineData(56, 0)]
    [InlineData(56, int.MaxValue)]
    public void ASegmentWhoseHeaderIsNotOneIsNotRead(int field, int value)
    {
        var (segment, _) = Committed("Program::Run");
        var damaged = Pinned(segment);
        if (field is 0 or 4 or 8 or 12)
            BinaryPrimitives.WriteInt32LittleEndian(damaged.AsSpan(field), value);
        else
            BinaryPrimitives.WriteInt64LittleEndian(damaged.AsSpan(field), value);

        Assert.Null(UnitSegment.Over(1, new PinnedUnitSegmentMemory(damaged)));
    }

    [Fact]
    public void ASegmentShorterThanItsHeaderIsNotRead() =>
        Assert.Null(UnitSegment.Over(1, new PinnedUnitSegmentMemory(Pinned(new byte[16]))));

    [Fact]
    public void ReferencesOutsideASegmentAreRefused()
    {
        var (segment, _) = Committed("Program::Run");
        using var read = UnitSegment.Over(1, new PinnedUnitSegmentMemory(Pinned(segment)))!;

        Assert.Throws<InvalidDataException>(() => read.Bytes(-1, 4).Length);
        Assert.Throws<InvalidDataException>(() => read.Bytes(read.Length - 2, 4).Length);
        Assert.Throws<InvalidDataException>(() => read.String(UnitStoreFormat.Pack(0, int.MaxValue)).Length);
        Assert.Throws<InvalidDataException>(() => read.Blob(UnitStoreFormat.Pack(0, int.MaxValue)).Length);
        Assert.Throws<InvalidDataException>(() => read.Query(read.QueryCount));
        read.Dispose();
        read.Dispose();
        Assert.Throws<ObjectDisposedException>(() => read.Bytes(0, 1).Length);
    }

    // Each manifest field it is checked by, set to a value no manifest has.
    [Theory]
    [InlineData(12, -1)]
    [InlineData(12, 0xFFFF)]
    [InlineData(16, -1)]
    [InlineData(20, -1)]
    [InlineData(20, 3)]
    [InlineData(20, 1 << 20)]
    public void AManifestWhoseShapeIsNotOneReadsAsNoUnits(int field, int value)
    {
        var (segment, change) = Committed("Program::Run");
        var manifest = change.Manifest!.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(manifest.AsSpan(field), value);

        using var store = UnitStore.Over(manifest, _ => new PinnedUnitSegmentMemory(Pinned(segment)));

        Assert.Equal(0, store.Count);
        Assert.Null(store.Find("Program::Run"));
    }

    // A segment that cannot be opened, is gone, or is not as long as the manifest says
    // holds no units; the store reads on without it.
    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("data")]
    [InlineData("unsupported")]
    [InlineData("missing")]
    [InlineData("length")]
    public void ASegmentThatCannotBeOpenedLeavesItsUnitsUnfound(string failure)
    {
        var (segment, change) = Committed("Program::Run");

        using var store = UnitStore.Over(change.Manifest!, _ => failure switch
        {
            "io" => throw new IOException("locked"),
            "access" => throw new UnauthorizedAccessException("denied"),
            "data" => throw new InvalidDataException("empty"),
            "unsupported" => throw new NotSupportedException("no mapping"),
            "missing" => null,
            _ => new PinnedUnitSegmentMemory(Pinned([.. segment, 0, 0, 0, 0, 0, 0, 0, 0])),
        });

        Assert.Null(store.Find("Program::Run"));
        Assert.Empty(store.LiveUnits());
    }

    // Every slot of the index is taken by other units, so a lookup probes them all.
    [Fact]
    public void ALookupOfAnIdentityNoFullSlotHoldsEnds()
    {
        var (segment, change) = Committed("Program::A", "Program::B");
        var manifest = change.Manifest!;
        var slots = BinaryPrimitives.ReadInt32LittleEndian(manifest.AsSpan(20));
        var index = UnitStoreFormat.ManifestHeaderSize + UnitStoreFormat.ManifestSegmentSize;
        var full = manifest.ToArray();
        var taken = full.AsSpan(index, UnitStoreFormat.SlotSize).ToArray();
        for (var slot = 0; slot < slots; slot++)
        {
            var entry = full.AsSpan(index + slot * UnitStoreFormat.SlotSize, UnitStoreFormat.SlotSize);
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]) == UnitStoreFormat.EmptySlot) taken.CopyTo(entry);
        }

        using var store = UnitStore.Over(full, _ => new PinnedUnitSegmentMemory(Pinned(segment)));

        Assert.Null(store.Find("Program::Missing"));
        Assert.NotNull(store.Find("Program::A"));
    }

    [Fact]
    public void ARecordWithNegativeCountsIsRefused()
    {
        var (segment, change) = Committed("Program::Run");
        using var read = UnitStore.Over(change.Manifest!, _ => new PinnedUnitSegmentMemory(Pinned(segment)));
        var location = read.Find("Program::Run")!.Value;
        var damaged = Pinned(segment);
        var records = BinaryPrimitives.ReadInt64LittleEndian(damaged.AsSpan(56));
        BinaryPrimitives.WriteInt32LittleEndian(damaged.AsSpan((int)records + location.Offset + 56), -1);
        using var corrupt = UnitSegment.Over(1, new PinnedUnitSegmentMemory(damaged))!;

        Assert.Throws<InvalidDataException>(() => { _ = corrupt.Record(location.Offset); });
    }

    // A record's exception groups read back as written; a group cut short or naming more
    // clauses than the record holds is refused.
    [Theory]
    [InlineData("intact")]
    [InlineData("short")]
    [InlineData("negative clauses")]
    [InlineData("too many clauses")]
    [InlineData("oversized")]
    public void ARecordsExceptionGroupsAndSizeAreCheckedAsRead(string damage)
    {
        var unit = Unit("Program::Run") with
        {
            ExceptionGroups = [new(0, -1, 0, 4, [0, 1]), new(1, 0, 2, 2, [])],
        };
        var writer = new UnitSegmentWriter();
        Assert.True(writer.Add(unit));
        var entries = writer.Entries.Select(entry => new UnitManifestEntry(entry.Hash, 0, entry.Offset)).ToArray();
        var memory = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(memory).Commit("app", new(1, writer,
            UnitManifestFormat.Write([new(1, writer.Count, writer.Length)], entries, 2), new HashSet<int> { 1 }, ""));
        var bytes = Pinned(memory.Segment("app", 1)!);
        var offset = entries[0].Offset;
        var record = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(56)) + offset;
        // One query (8 bytes of ids, aligned) follows the fixed record; the groups then.
        var groups = record + UnitStoreFormat.FixedRecordSize + 8;
        switch (damage)
        {
            case "short": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 68), 3); break;
            case "negative clauses": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(groups + 16), -1); break;
            case "too many clauses": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(groups + 16), 1000); break;
            case "oversized": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 56), int.MaxValue / 8); break;
        }
        using var segment = UnitSegment.Over(1, new PinnedUnitSegmentMemory(bytes))!;

        if (damage == "intact")
        {
            Assert.Equal(unit.ExceptionGroups.Value, segment.Record(offset).ExceptionGroups(), UnitExceptionGroupComparer.Instance);
            return;
        }
        Assert.Throws<InvalidDataException>(() => { _ = segment.Record(offset).ExceptionGroups(); });
    }

    [Fact]
    public void AUnitWithMoreParametersThanARecordHoldsIsNotWritten() =>
        Assert.False(new UnitSegmentWriter().Add(Unit("Program::Run") with
        {
            Parameters = [.. Enumerable.Repeat(NetWasm.Compiler.Core.CliValueKind.I4, ushort.MaxValue + 1)],
        }));

    private sealed class UnitExceptionGroupComparer : IEqualityComparer<UnitExceptionGroup>
    {
        public static UnitExceptionGroupComparer Instance { get; } = new();

        public bool Equals(UnitExceptionGroup x, UnitExceptionGroup y) =>
            x.Id == y.Id && x.Parent == y.Parent && x.TryOffset == y.TryOffset && x.TryLength == y.TryLength &&
            x.Regions.SequenceEqual(y.Regions);

        public int GetHashCode(UnitExceptionGroup obj) => obj.Id;
    }

    [Fact]
    public void ARecordWithoutARootMapReadsNone()
    {
        var (segment, change) = Committed("Program::Run");
        using var store = UnitStore.Over(change.Manifest!, _ => new PinnedUnitSegmentMemory(Pinned(segment)));
        var location = store.Find("Program::Run")!.Value;
        var record = store.Segments[location.Segment]!.Record(location.Offset);

        Assert.False(record.HasRootMap);
        Assert.True(record.RootMapBytes.IsEmpty);
        var refused = false;
        try
        {
            _ = record.RootMapQueryId(0);
        }
        catch (ArgumentOutOfRangeException)
        {
            refused = true;
        }
        Assert.True(refused);
    }

    // A segment holds each query once; a unit whose root map was answered otherwise than the
    // segment's units were is not written into it.
    [Fact]
    public void AUnitWhoseRootMapAnswersDisagreeIsNotWritten()
    {
        var writer = new UnitSegmentWriter();
        Assert.True(writer.Add(Unit("Program::A")));
        var unit = Unit("Program::B") with
        {
            Dependencies = [],
            RootMap = [1, 2, 3, 4],
            RootMapDependencies = [new(UnitQueryKind.StringLayout, "hello", new(9, 9))],
        };

        Assert.False(writer.Add(unit));
    }
}

// A store's files beside a compile, and the archive a browser keeps one in.
public sealed class UnitStoreMediaTests
{
    private static readonly UnitKeptCompile Kept = new([1], [2], [3], [4]);

    [Theory]
    [InlineData("units-3.seg", true)]
    [InlineData("units-x.seg", false)]
    [InlineData("units-3.bin", false)]
    [InlineData("other-3.seg", false)]
    public void ASegmentFileIsNamedByItsSequence(string name, bool parses)
    {
        Assert.Equal(parses, UnitStoreFiles.TryParseSequence(Path.Combine("store", name), out var sequence));
        Assert.Equal(parses ? 3 : 0, sequence);
    }

    [Fact]
    public void AKeptCompileIsReadBackFromItsFilesAndNoneIsReadWhereThereIsNone()
    {
        var location = Path.Combine(Path.GetTempPath(), "netwasm-kept-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(new FileUnitKeptCompileLoader().Load(location));
            new FileUnitKeptCompileCommitter().Commit(location, Kept);

            var read = new FileUnitKeptCompileLoader().Load(location);

            Assert.NotNull(read);
            Assert.Equal(Kept.Module, read.Module);
            Assert.Equal(Kept.Facts, read.Facts);
        }
        finally
        {
            if (Directory.Exists(location)) Directory.Delete(location, recursive: true);
        }
    }

    [Fact]
    public void AnEmptySegmentFileIsNotMapped()
    {
        var path = Path.Combine(Path.GetTempPath(), "netwasm-empty-" + Guid.NewGuid().ToString("N") + ".seg");
        File.WriteAllBytes(path, []);
        try
        {
            Assert.Throws<InvalidDataException>(() => new MappedUnitSegmentMemory(path, new NoUnitMemoryPrefetcher()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnArchiveCarriesALocationsUnitsAndKeptCompileAlone()
    {
        var memory = new UnitStoreMemory();
        var (change, other) = (Change(1), Change(1));
        new MemoryUnitStoreCommitter(memory).Commit("app", change);
        new MemoryUnitStoreCommitter(memory).Commit("other", other);
        memory.SetInputs("app", [7], "report");
        memory.Keep("app", Kept);

        var restored = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(restored).Commit("other", other);
        restored.Import("app", memory.Export("app"));

        Assert.Equal(memory.Manifest("app"), restored.Manifest("app"));
        Assert.Equal(memory.Segment("app", 1), restored.Segment("app", 1));
        Assert.Equal([7], restored.Inputs("app"));
        Assert.Equal(Kept.Plans, restored.Kept("app")!.Plans);
        Assert.NotNull(restored.Manifest("other"));
    }

    [Fact]
    public void AnArchiveWithoutAKeptCompileOrInputsImportsTheUnitsAlone()
    {
        var memory = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(memory).Commit("app", Change(1));

        var restored = new UnitStoreMemory();
        restored.Import("app", memory.Export("app"));

        Assert.NotNull(restored.Manifest("app"));
        Assert.Null(restored.Inputs("app"));
        Assert.Null(restored.Kept("app"));
    }

    // An archive of another format, or one cut short, leaves the location empty.
    [Theory]
    [InlineData("magic")]
    [InlineData("version")]
    [InlineData("truncated")]
    [InlineData("length")]
    [InlineData("segment")]
    [InlineData("empty")]
    public void AnArchiveThatIsNotOneLeavesTheLocationEmpty(string damage)
    {
        var memory = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(memory).Commit("app", Change(1));
        memory.Keep("app", Kept);
        var archive = memory.Export("app");
        var segmentLength = archive.Length - 4 - 4 - memory.Segment("app", 1)!.Length;
        byte[] damaged = damage switch
        {
            "magic" => [(byte)(archive[0] ^ 1), .. archive[1..]],
            "version" => [.. archive[..4], (byte)(archive[4] + 1), .. archive[5..]],
            "truncated" => archive[..^3],
            "length" => [.. archive[..8], .. BitConverter.GetBytes(int.MaxValue), .. archive[12..]],
            "segment" => [.. archive[..(segmentLength + 4)], .. BitConverter.GetBytes(-1)],
            _ => [],
        };
        memory.Import("app", damaged);

        Assert.Null(memory.Manifest("app"));
        Assert.Null(memory.Segment("app", 1));
        Assert.Null(memory.Kept("app"));
    }

    private static UnitStoreChange Change(int sequence)
    {
        var writer = new UnitSegmentWriter();
        writer.Add(new CompiledUnit("Program::Run", new(1, 2), [], [0x00, 0x0B], [], 1) { FunctionName = "run" });
        var entries = writer.Entries.Select(entry => new UnitManifestEntry(entry.Hash, 0, entry.Offset)).ToArray();
        return new(sequence, writer, UnitManifestFormat.Write([new(sequence, writer.Count, writer.Length)], entries, sequence + 1),
            new HashSet<int> { sequence }, "summary");
    }
}
