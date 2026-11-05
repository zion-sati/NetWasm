using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// The unit store's binary layout (v5), little-endian. A store is a manifest and immutable
/// segments. Strings are UTF-16 so they are read as spans of char in place; a string or
/// body reference packs {offset, length} into 64 bits.
///
/// Segment: a 64-byte header (magic, version, kind, query count, then the offsets of the
/// string table, its length in chars, the body blob, its length, the query table and the
/// records); the string table; the blob; queries of 32 bytes (kind u16, pad, key ref,
/// answer); records. A record is 72 fixed bytes (identity ref, input fingerprint,
/// instruction count, result, flags, parameter count, function name ref, environment key
/// ref, body ref, relocation, query and block counts, exception-group words), then
/// parameters (u8, padded to 8), relocations of 16 bytes (offset i32, length, space, signed,
/// pad, symbol ref), query ids (u32, padded to 8), blocks (block i32, count i32) and the
/// method's exception groups as i32 words (padded to 8): per group its id, parent (-1 for
/// none), try offset and length, clause count and each clause's index among the body's
/// exception regions, in the order planning enumerates them. -1 words: the method cannot
/// stand in for its structured form (filter clauses, or clauses that are not regions).
/// Then the ids of the queries the method's root-map analysis made (u32, padded to 8); the
/// root map itself is a body-blob reference at 72, present when flag 4 is set.
///
/// Manifest: a 32-byte header (magic, version, kind, segment count, unit count, slot
/// count, next sequence); segments of 16 bytes (sequence, units when written, length);
/// an open-addressing index of 16-byte slots (XXH3-64 of the identity's UTF-16 bytes,
/// segment ordinal u16 with 0xFFFF for an empty slot, pad, record offset u32).
/// </summary>
internal static class UnitStoreFormat
{
    public const uint Magic = 0x5554574E; // "NWTU"
    // Raised whenever units come to record more of what emission reads, so older units,
    // validated against less, are not restored.
    public const int Version = 12;
    public const uint SegmentKind = 1;
    public const uint ManifestKind = 2;
    public const int SegmentHeaderSize = 64;
    public const int ManifestHeaderSize = 32;
    public const int ManifestSegmentSize = 16;
    public const int SlotSize = 16;
    public const int FixedRecordSize = 88;
    public const int RelocationSize = 16;
    public const int QuerySize = 40;
    public const int BlockSize = 8;
    public const ushort EmptySlot = ushort.MaxValue;

    // Strings are read in the host's UTF-16; every supported host is little-endian.
    public static bool IsSupported => BitConverter.IsLittleEndian;

    public static ulong HashOf(ReadOnlySpan<char> text) =>
        XxHash3.HashToUInt64(MemoryMarshal.AsBytes(text));

    public static int Align8(int value) => (value + 7) & ~7;

    public static long Pack(int offset, int length) => (uint)offset | ((long)length << 32);

    public static (int Offset, int Length) Unpack(long reference) =>
        ((int)(uint)reference, (int)(reference >>> 32));
}

/// <summary>Where a unit is: a manifest segment ordinal and a record offset in it.</summary>
internal readonly record struct UnitLocation(int Segment, int Offset);

/// <summary>A segment's bytes, wherever they are held; never in movable managed memory.</summary>
internal abstract unsafe class UnitSegmentMemory : IDisposable
{
    public abstract byte* Pointer { get; }
    public abstract long Length { get; }
    public abstract void Dispose();
}

/// <summary>One immutable segment, read in place.</summary>
internal sealed unsafe class UnitSegment : IDisposable
{
    // Per query, for the current compile: whether its recorded answer still holds.
    internal const int Unknown = 0;
    internal const int Valid = 1;
    internal const int Unbuildable = 2;
    internal const int Failed = 3;
    internal const int Changed = 4;

    private UnitSegmentMemory? _memory;
    private readonly byte* _data;
    private readonly long _length;
    private readonly long _strings;
    private readonly long _stringChars;
    private readonly long _blob;
    private readonly long _blobLength;
    private readonly long _queries;
    private readonly int[] _queryStates;

    private UnitSegment(int sequence, UnitSegmentMemory memory, int queryCount, long strings,
        long stringChars, long blob, long blobLength, long queries, long records)
    {
        Sequence = sequence;
        _memory = memory;
        _data = memory.Pointer;
        _length = memory.Length;
        _strings = strings;
        _stringChars = stringChars;
        _blob = blob;
        _blobLength = blobLength;
        _queries = queries;
        RecordsOffset = records;
        _queryStates = new int[queryCount];
    }

    internal int Sequence { get; }
    internal long Length => _length;
    internal int QueryCount => _queryStates.Length;
    internal long RecordsOffset { get; }

    // A segment over its bytes, or null (with the memory released) when they are not one.
    internal static UnitSegment? Over(int sequence, UnitSegmentMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (memory.Length < UnitStoreFormat.SegmentHeaderSize)
        {
            memory.Dispose();
            return null;
        }
        var header = new ReadOnlySpan<byte>(memory.Pointer, UnitStoreFormat.SegmentHeaderSize);
        var queryCount = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        var strings = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
        var stringChars = BinaryPrimitives.ReadInt64LittleEndian(header[24..]);
        var blob = BinaryPrimitives.ReadInt64LittleEndian(header[32..]);
        var blobLength = BinaryPrimitives.ReadInt64LittleEndian(header[40..]);
        var queries = BinaryPrimitives.ReadInt64LittleEndian(header[48..]);
        var records = BinaryPrimitives.ReadInt64LittleEndian(header[56..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != UnitStoreFormat.Magic ||
            BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != UnitStoreFormat.Version ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != UnitStoreFormat.SegmentKind ||
            queryCount < 0 || strings < UnitStoreFormat.SegmentHeaderSize || stringChars < 0 ||
            blob < strings + stringChars * 2 || blobLength < 0 || queries < blob + blobLength ||
            records < queries + (long)queryCount * UnitStoreFormat.QuerySize || records > memory.Length)
        {
            memory.Dispose();
            return null;
        }
        return new(sequence, memory, queryCount, strings, stringChars, blob, blobLength, queries, records);
    }

    internal ReadOnlySpan<byte> Bytes(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(_memory is null, this);
        if (offset < 0 || length < 0 || offset > _length - length)
            throw new InvalidDataException("A unit segment is truncated or corrupt.");
        return new ReadOnlySpan<byte>(_data + offset, length);
    }

    internal ReadOnlySpan<char> String(long reference)
    {
        ObjectDisposedException.ThrowIf(_memory is null, this);
        var (offset, length) = UnitStoreFormat.Unpack(reference);
        if (offset < 0 || length < 0 || offset > _stringChars - length)
            throw new InvalidDataException("A unit segment string is out of range.");
        return new ReadOnlySpan<char>(_data + _strings + offset * 2L, length);
    }

    internal ReadOnlySpan<byte> Blob(long reference)
    {
        var (offset, length) = UnitStoreFormat.Unpack(reference);
        if (offset < 0 || length < 0 || offset > _blobLength - length)
            throw new InvalidDataException("A unit segment body is out of range.");
        return Bytes(_blob + offset, length);
    }

    internal UnitRecord Record(int offset) => new(this, offset);

    internal UnitQuery Query(int id)
    {
        if ((uint)id >= (uint)_queryStates.Length)
            throw new InvalidDataException("A unit refers to a query its segment does not hold.");
        var bytes = Bytes(_queries + (long)id * UnitStoreFormat.QuerySize, UnitStoreFormat.QuerySize);
        var value = BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]);
        return new((UnitQueryKind)BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            String(BinaryPrimitives.ReadInt64LittleEndian(bytes[8..])),
            new(BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..])),
            value == 0 ? default : Blob(value));
    }

    internal ref int QueryState(int id) => ref _queryStates[id];

    public void Dispose()
    {
        _memory?.Dispose();
        _memory = null;
    }
}

/// <summary>A recorded query as its segment holds it.</summary>
internal readonly ref struct UnitQuery(UnitQueryKind kind, ReadOnlySpan<char> key, UnitFingerprint answer,
    ReadOnlySpan<byte> value)
{
    public UnitQueryKind Kind { get; } = kind;
    public ReadOnlySpan<char> Key { get; } = key;
    public UnitFingerprint Answer { get; } = answer;
    // The answer as encoded (UnitAnswerCodec); empty when its kind keeps no value.
    public ReadOnlySpan<byte> Value { get; } = value;

    public UnitDependency ToDependency() => new(Kind, Key.ToString(), Answer)
    {
        Value = Value.IsEmpty ? null : new UnitEncodedValue(Value.ToArray()),
    };
}

internal readonly record struct UnitRelocationRecord(int Offset, int Length, WasmIndexSpace Space, bool IsSigned33);

/// <summary>A unit record read in place; strings and the body are views of the segment.</summary>
internal readonly ref struct UnitRecord
{
    private readonly UnitSegment _segment;
    private readonly ReadOnlySpan<byte> _bytes;

    public UnitRecord(UnitSegment segment, int offset)
    {
        _segment = segment;
        var start = segment.RecordsOffset + offset;
        var head = segment.Bytes(start, UnitStoreFormat.FixedRecordSize);
        _bytes = segment.Bytes(start, SizeOf(head));
    }

    public ReadOnlySpan<char> Identity => _segment.String(Ref(0));
    public UnitFingerprint Input => new(
        BinaryPrimitives.ReadUInt64LittleEndian(_bytes[8..]),
        BinaryPrimitives.ReadUInt64LittleEndian(_bytes[16..]));
    public int InstructionCount => BinaryPrimitives.ReadInt32LittleEndian(_bytes[24..]);
    public NetWasm.Compiler.Core.CliValueKind Result => (NetWasm.Compiler.Core.CliValueKind)_bytes[28];
    public bool IsAggressiveInlining => (_bytes[29] & 1) != 0;
    public bool IsNoInlining => (_bytes[29] & 2) != 0;
    public bool HasRootMap => (_bytes[29] & 4) != 0;
    public ReadOnlySpan<byte> RootMapBytes => HasRootMap ? _segment.Blob(Ref(72)) : default;
    public int RootMapQueryCount => BinaryPrimitives.ReadInt32LittleEndian(_bytes[80..]);
    public int RootMapQueryId(int index) => BinaryPrimitives.ReadInt32LittleEndian(
        _bytes[(RootMapQueriesStart + Checked(index, RootMapQueryCount) * 4)..]);

    public ImmutableArray<UnitDependency> RootMapDependencies()
    {
        var dependencies = ImmutableArray.CreateBuilder<UnitDependency>(RootMapQueryCount);
        for (var index = 0; index < RootMapQueryCount; index++)
        {
            var query = _segment.Query(RootMapQueryId(index));
            dependencies.Add(query.ToDependency());
        }
        return dependencies.MoveToImmutable();
    }
    public int ParameterCount => BinaryPrimitives.ReadUInt16LittleEndian(_bytes[30..]);
    public ReadOnlySpan<char> FunctionName => _segment.String(Ref(32));
    public ReadOnlySpan<char> EnvironmentKey => _segment.String(Ref(40));
    public ReadOnlySpan<byte> Body => _segment.Blob(Ref(48));
    public int RelocationCount => BinaryPrimitives.ReadInt32LittleEndian(_bytes[56..]);
    public int QueryCount => BinaryPrimitives.ReadInt32LittleEndian(_bytes[60..]);
    public int BlockCount => BinaryPrimitives.ReadInt32LittleEndian(_bytes[64..]);
    public int GroupWords => BinaryPrimitives.ReadInt32LittleEndian(_bytes[68..]);
    // Whether the unit records its method's exception groups, so planning can use them
    // without structuring the method.
    public bool HasExceptionGroups => GroupWords >= 0;
    public int Size => _bytes.Length;

    public ImmutableArray<UnitExceptionGroup> ExceptionGroups()
    {
        var words = GroupWords;
        if (words <= 0) return [];
        var start = BlocksStart + BlockCount * UnitStoreFormat.BlockSize;
        var section = _bytes.Slice(start, words * 4);
        var groups = ImmutableArray.CreateBuilder<UnitExceptionGroup>();
        var at = 0;
        while (at < words)
        {
            if (at + 5 > words) throw new InvalidDataException("A unit's exception groups are corrupt.");
            var clauses = Word(section, at + 4);
            if (clauses < 0 || at + 5 + clauses > words) throw new InvalidDataException("A unit's exception groups are corrupt.");
            var regions = ImmutableArray.CreateBuilder<int>(clauses);
            for (var clause = 0; clause < clauses; clause++) regions.Add(Word(section, at + 5 + clause));
            groups.Add(new(Word(section, at), Word(section, at + 1), Word(section, at + 2), Word(section, at + 3),
                regions.MoveToImmutable()));
            at += 5 + clauses;
        }
        return groups.ToImmutable();
    }

    public NetWasm.Compiler.Core.CliValueKind Parameter(int index) =>
        (NetWasm.Compiler.Core.CliValueKind)_bytes[UnitStoreFormat.FixedRecordSize + Checked(index, ParameterCount)];

    public UnitRelocationRecord Relocation(int index)
    {
        var at = RelocationsStart + Checked(index, RelocationCount) * UnitStoreFormat.RelocationSize;
        return new(BinaryPrimitives.ReadInt32LittleEndian(_bytes[at..]), _bytes[at + 4],
            (WasmIndexSpace)_bytes[at + 5], _bytes[at + 6] != 0);
    }

    public ReadOnlySpan<char> RelocationSymbol(int index) => _segment.String(BinaryPrimitives.ReadInt64LittleEndian(
        _bytes[(RelocationsStart + Checked(index, RelocationCount) * UnitStoreFormat.RelocationSize + 8)..]));

    public int QueryId(int index) => BinaryPrimitives.ReadInt32LittleEndian(
        _bytes[(QueriesStart + Checked(index, QueryCount) * 4)..]);

    public (int Block, int Count) Block(int index)
    {
        var at = BlocksStart + Checked(index, BlockCount) * UnitStoreFormat.BlockSize;
        return (BinaryPrimitives.ReadInt32LittleEndian(_bytes[at..]),
            BinaryPrimitives.ReadInt32LittleEndian(_bytes[(at + 4)..]));
    }

    // The unit as a managed value, for compaction and tests; restore never needs it.
    public CompiledUnit ToUnit()
    {
        var parameters = ImmutableArray.CreateBuilder<NetWasm.Compiler.Core.CliValueKind>(ParameterCount);
        for (var index = 0; index < ParameterCount; index++) parameters.Add(Parameter(index));
        var relocations = ImmutableArray.CreateBuilder<UnitRelocation>(RelocationCount);
        for (var index = 0; index < RelocationCount; index++)
        {
            var relocation = Relocation(index);
            relocations.Add(new(relocation.Offset, relocation.Length, relocation.Space,
                RelocationSymbol(index).ToString(), relocation.IsSigned33));
        }
        var dependencies = ImmutableArray.CreateBuilder<UnitDependency>(QueryCount);
        for (var index = 0; index < QueryCount; index++)
        {
            var query = _segment.Query(QueryId(index));
            dependencies.Add(query.ToDependency());
        }
        var blocks = ImmutableDictionary.CreateBuilder<int, int>();
        for (var index = 0; index < BlockCount; index++)
        {
            var (block, count) = Block(index);
            blocks[block] = count;
        }
        return new(Identity.ToString(), Input, dependencies.MoveToImmutable(), [.. Body],
            relocations.MoveToImmutable(), InstructionCount)
        {
            FunctionName = FunctionName.ToString(),
            Parameters = parameters.MoveToImmutable(),
            Result = Result,
            IsAggressiveInlining = IsAggressiveInlining,
            IsNoInlining = IsNoInlining,
            EnvironmentKey = EnvironmentKey.ToString(),
            BlockEmissionCounts = blocks.ToImmutable(),
            ExceptionGroups = HasExceptionGroups ? ExceptionGroups() : null,
            RootMap = HasRootMap ? [.. RootMapBytes] : null,
            RootMapDependencies = RootMapDependencies(),
        };
    }

    private static int Word(ReadOnlySpan<byte> words, int index) => BinaryPrimitives.ReadInt32LittleEndian(words[(index * 4)..]);

    private int RelocationsStart => UnitStoreFormat.FixedRecordSize + UnitStoreFormat.Align8(ParameterCount);
    private int QueriesStart => RelocationsStart + RelocationCount * UnitStoreFormat.RelocationSize;
    private int BlocksStart => QueriesStart + UnitStoreFormat.Align8(QueryCount * 4);
    private int RootMapQueriesStart => BlocksStart + BlockCount * UnitStoreFormat.BlockSize +
        UnitStoreFormat.Align8(Math.Max(0, GroupWords) * 4);
    private long Ref(int at) => BinaryPrimitives.ReadInt64LittleEndian(_bytes[at..]);

    private static int Checked(int index, int count) =>
        (uint)index < (uint)count ? index : throw new ArgumentOutOfRangeException(nameof(index));

    private static int SizeOf(ReadOnlySpan<byte> head)
    {
        var parameters = BinaryPrimitives.ReadUInt16LittleEndian(head[30..]);
        var relocations = (long)BinaryPrimitives.ReadInt32LittleEndian(head[56..]);
        var queries = (long)BinaryPrimitives.ReadInt32LittleEndian(head[60..]);
        var blocks = (long)BinaryPrimitives.ReadInt32LittleEndian(head[64..]);
        var groupWords = (long)Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(head[68..]));
        var rootMapQueries = (long)BinaryPrimitives.ReadInt32LittleEndian(head[80..]);
        if (relocations < 0 || queries < 0 || blocks < 0 || rootMapQueries < 0)
            throw new InvalidDataException("A unit record is corrupt.");
        var size = UnitStoreFormat.FixedRecordSize + UnitStoreFormat.Align8(parameters) +
            relocations * UnitStoreFormat.RelocationSize + ((queries * 4 + 7) & ~7L) +
            blocks * UnitStoreFormat.BlockSize + ((groupWords * 4 + 7) & ~7L) + ((rootMapQueries * 4 + 7) & ~7L);
        return size <= int.MaxValue ? (int)size : throw new InvalidDataException("A unit record is corrupt.");
    }
}

/// <summary>
/// The units of the last compile: the manifest's index over its live segments. Disposing it
/// releases every segment's memory.
/// </summary>
internal sealed class UnitStore : IDisposable
{
    private readonly byte[] _manifest;
    private readonly int _slots;
    private readonly int _index;
    private readonly UnitSegment?[] _segments;
    private readonly int[] _unitsAtWrite;

    private UnitStore(byte[] manifest, int slots, int index, UnitSegment?[] segments, int[] unitsAtWrite,
        int count, int nextSequence)
    {
        _manifest = manifest;
        _slots = slots;
        _index = index;
        _segments = segments;
        _unitsAtWrite = unitsAtWrite;
        Count = count;
        NextSequence = nextSequence;
    }

    internal static UnitStore Empty => new([], 0, 0, [], [], 0, 1);

    internal int Count { get; }
    internal int NextSequence { get; }
    internal IReadOnlyList<UnitSegment?> Segments => _segments;

    // How many units a segment held when it was written.
    internal int UnitsAtWrite(int segment) => _unitsAtWrite[segment];

    // The store a manifest describes, opening each segment it lists; a segment that is
    // missing, of another length or not a segment leaves its units unfound.
    internal static UnitStore Over(byte[] manifest, Func<int, UnitSegmentMemory?> open)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(open);
        var span = manifest.AsSpan();
        if (!UnitStoreFormat.IsSupported || span.Length < UnitStoreFormat.ManifestHeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(span) != UnitStoreFormat.Magic ||
            BinaryPrimitives.ReadInt32LittleEndian(span[4..]) != UnitStoreFormat.Version ||
            BinaryPrimitives.ReadUInt32LittleEndian(span[8..]) != UnitStoreFormat.ManifestKind)
        {
            return Empty;
        }
        var segmentCount = BinaryPrimitives.ReadInt32LittleEndian(span[12..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(span[16..]);
        var slots = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);
        var nextSequence = BinaryPrimitives.ReadInt32LittleEndian(span[24..]);
        var index = UnitStoreFormat.ManifestHeaderSize + (long)segmentCount * UnitStoreFormat.ManifestSegmentSize;
        if (segmentCount is < 0 or >= UnitStoreFormat.EmptySlot || count < 0 || slots < 0 ||
            (slots & (slots - 1)) != 0 || index + (long)slots * UnitStoreFormat.SlotSize != span.Length)
        {
            return Empty;
        }
        var segments = new UnitSegment?[segmentCount];
        var unitsAtWrite = new int[segmentCount];
        for (var ordinal = 0; ordinal < segmentCount; ordinal++)
        {
            var entry = span.Slice(UnitStoreFormat.ManifestHeaderSize + ordinal * UnitStoreFormat.ManifestSegmentSize,
                UnitStoreFormat.ManifestSegmentSize);
            var sequence = BinaryPrimitives.ReadInt32LittleEndian(entry);
            unitsAtWrite[ordinal] = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            var length = BinaryPrimitives.ReadInt64LittleEndian(entry[8..]);
            UnitSegmentMemory? memory;
            try
            {
                memory = open(sequence);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or NotSupportedException)
            {
                continue;
            }
            if (memory is null)
                continue;
            if (memory.Length != length)
            {
                memory.Dispose();
                continue;
            }
            segments[ordinal] = UnitSegment.Over(sequence, memory);
        }
        return new(manifest, slots, (int)index, segments, unitsAtWrite, count, nextSequence);
    }

    // Every unit the manifest lists: the units the last compile used.
    internal IEnumerable<UnitLocation> LiveUnits()
    {
        for (var slot = 0; slot < _slots; slot++)
        {
            var entry = _manifest.AsSpan(_index + slot * UnitStoreFormat.SlotSize, UnitStoreFormat.SlotSize);
            var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]);
            if (ordinal == UnitStoreFormat.EmptySlot || ordinal >= _segments.Length || _segments[ordinal] is null)
                continue;
            yield return new(ordinal, (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]));
        }
    }

    internal UnitLocation? Find(ReadOnlySpan<char> identity)
    {
        if (_slots == 0)
            return null;
        var hash = UnitStoreFormat.HashOf(identity);
        var mask = _slots - 1;
        var slot = (int)(hash & (ulong)mask);
        for (var probe = 0; probe < _slots; probe++, slot = (slot + 1) & mask)
        {
            var entry = _manifest.AsSpan(_index + slot * UnitStoreFormat.SlotSize, UnitStoreFormat.SlotSize);
            var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]);
            if (ordinal == UnitStoreFormat.EmptySlot)
                return null;
            if (BinaryPrimitives.ReadUInt64LittleEndian(entry) != hash ||
                ordinal >= _segments.Length || _segments[ordinal] is not { } segment)
            {
                continue;
            }
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (segment.Record(offset).Identity.SequenceEqual(identity))
                return new(ordinal, offset);
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var segment in _segments) segment?.Dispose();
    }
}

/// <summary>A segment's place in a manifest being written.</summary>
internal readonly record struct UnitManifestSegment(int Sequence, int UnitsAtWrite, long Length);

/// <summary>An index entry of a manifest being written.</summary>
internal readonly record struct UnitManifestEntry(ulong Hash, int Segment, int Offset);

internal static class UnitManifestFormat
{
    public static byte[] Write(IReadOnlyList<UnitManifestSegment> segments,
        IReadOnlyList<UnitManifestEntry> entries, int nextSequence)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(entries);
        var slots = 0;
        if (entries.Count != 0)
        {
            slots = 16;
            while (slots < entries.Count * 2) slots <<= 1;
        }
        var index = UnitStoreFormat.ManifestHeaderSize + segments.Count * UnitStoreFormat.ManifestSegmentSize;
        var bytes = new byte[index + slots * UnitStoreFormat.SlotSize];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, UnitStoreFormat.Magic);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], UnitStoreFormat.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], UnitStoreFormat.ManifestKind);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], segments.Count);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], entries.Count);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], slots);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], nextSequence);
        for (var ordinal = 0; ordinal < segments.Count; ordinal++)
        {
            var entry = span[(UnitStoreFormat.ManifestHeaderSize + ordinal * UnitStoreFormat.ManifestSegmentSize)..];
            BinaryPrimitives.WriteInt32LittleEndian(entry, segments[ordinal].Sequence);
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], segments[ordinal].UnitsAtWrite);
            BinaryPrimitives.WriteInt64LittleEndian(entry[8..], segments[ordinal].Length);
        }
        for (var slot = 0; slot < slots; slot++)
            BinaryPrimitives.WriteUInt16LittleEndian(span[(index + slot * UnitStoreFormat.SlotSize + 8)..], UnitStoreFormat.EmptySlot);
        var mask = slots - 1;
        foreach (var entry in entries)
        {
            var slot = (int)(entry.Hash & (ulong)mask);
            while (BinaryPrimitives.ReadUInt16LittleEndian(span[(index + slot * UnitStoreFormat.SlotSize + 8)..]) !=
                   UnitStoreFormat.EmptySlot)
            {
                slot = (slot + 1) & mask;
            }
            var target = span.Slice(index + slot * UnitStoreFormat.SlotSize, UnitStoreFormat.SlotSize);
            BinaryPrimitives.WriteUInt64LittleEndian(target, entry.Hash);
            BinaryPrimitives.WriteUInt16LittleEndian(target[8..], checked((ushort)entry.Segment));
            BinaryPrimitives.WriteUInt32LittleEndian(target[12..], checked((uint)entry.Offset));
        }
        return bytes;
    }
}

/// <summary>
/// Builds one segment. Every unit in a segment was checked against the same compile, so a
/// query has one answer in it and is stored once; a unit whose answer disagrees is refused.
/// </summary>
internal sealed class UnitSegmentWriter
{
    private readonly ArrayBufferWriter<char> _strings = new(1 << 15);
    private readonly Dictionary<string, long> _interned = new(StringComparer.Ordinal);
    private readonly ArrayBufferWriter<byte> _blob = new(1 << 16);
    private readonly ArrayBufferWriter<byte> _queries = new(1 << 12);
    private readonly Dictionary<(UnitQueryKind Kind, long Key), (int Id, UnitFingerprint Answer)> _queryIds = [];
    private readonly ArrayBufferWriter<byte> _records = new(1 << 16);
    private readonly HashSet<string> _identities = new(StringComparer.Ordinal);
    private readonly List<(ulong Hash, int Offset)> _entries = [];

    internal int Count => _entries.Count;

    // The units in the segment by identity hash and record offset.
    internal IReadOnlyList<(ulong Hash, int Offset)> Entries => _entries;

    // The segment's length once written.
    internal long Length => Align8(Align8(UnitStoreFormat.SegmentHeaderSize + _strings.WrittenCount * 2L) +
        _blob.WrittenCount) + _queries.WrittenCount + _records.WrittenCount;

    internal bool Add(CompiledUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_identities.Contains(unit.Identity))
            return false;
        if (!TryKeys(unit.Dependencies, out var keys))
            return false;
        var rootMapDependencies = unit.RootMap is null ? [] : unit.RootMapDependencies;
        if (!TryKeys(rootMapDependencies, out var rootMapKeys))
            return false;
        var rootMap = unit.RootMap ?? [];
        var groupWords = GroupWords(unit.ExceptionGroups);
        var size = UnitStoreFormat.FixedRecordSize + UnitStoreFormat.Align8(unit.Parameters.Length) +
            (long)unit.Relocations.Length * UnitStoreFormat.RelocationSize +
            UnitStoreFormat.Align8(keys.Length * 4) + (long)unit.BlockEmissionCounts.Count * UnitStoreFormat.BlockSize +
            UnitStoreFormat.Align8(Math.Max(0, groupWords) * 4) + UnitStoreFormat.Align8(rootMapKeys.Length * 4);
        if (_records.WrittenCount + size > int.MaxValue ||
            _blob.WrittenCount + (long)unit.Body.Length + rootMap.Length > int.MaxValue ||
            unit.Parameters.Length > ushort.MaxValue)
        {
            return false;
        }
        var offset = _records.WrittenCount;
        var record = _records.GetSpan((int)size)[..(int)size];
        record.Clear();
        WriteRef(record, 0, Intern(unit.Identity));
        BinaryPrimitives.WriteUInt64LittleEndian(record[8..], unit.Input.First);
        BinaryPrimitives.WriteUInt64LittleEndian(record[16..], unit.Input.Second);
        BinaryPrimitives.WriteInt32LittleEndian(record[24..], unit.InstructionCount);
        record[28] = (byte)unit.Result;
        record[29] = (byte)((unit.IsAggressiveInlining ? 1 : 0) | (unit.IsNoInlining ? 2 : 0) |
            (unit.RootMap is not null ? 4 : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(record[30..], (ushort)unit.Parameters.Length);
        WriteRef(record, 32, Intern(unit.FunctionName));
        WriteRef(record, 40, Intern(unit.EnvironmentKey));
        var body = _blob.WrittenCount;
        _blob.Write(unit.Body.AsSpan());
        WriteRef(record, 48, UnitStoreFormat.Pack(body, unit.Body.Length));
        BinaryPrimitives.WriteInt32LittleEndian(record[56..], unit.Relocations.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record[60..], keys.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record[64..], unit.BlockEmissionCounts.Count);
        BinaryPrimitives.WriteInt32LittleEndian(record[68..], groupWords);
        if (unit.RootMap is not null)
        {
            var map = _blob.WrittenCount;
            _blob.Write(rootMap.AsSpan());
            WriteRef(record, 72, UnitStoreFormat.Pack(map, rootMap.Length));
        }
        BinaryPrimitives.WriteInt32LittleEndian(record[80..], rootMapKeys.Length);
        var at = UnitStoreFormat.FixedRecordSize;
        for (var index = 0; index < unit.Parameters.Length; index++)
            record[at + index] = (byte)unit.Parameters[index];
        at += UnitStoreFormat.Align8(unit.Parameters.Length);
        foreach (var relocation in unit.Relocations)
        {
            BinaryPrimitives.WriteInt32LittleEndian(record[at..], relocation.Offset);
            record[at + 4] = (byte)relocation.Length;
            record[at + 5] = (byte)relocation.Space;
            record[at + 6] = relocation.IsSigned33 ? (byte)1 : (byte)0;
            WriteRef(record, at + 8, Intern(relocation.Symbol));
            at += UnitStoreFormat.RelocationSize;
        }
        for (var index = 0; index < keys.Length; index++, at += 4)
        {
            BinaryPrimitives.WriteInt32LittleEndian(record[at..],
                QueryId(unit.Dependencies[index], keys[index]));
        }
        at = UnitStoreFormat.FixedRecordSize + UnitStoreFormat.Align8(unit.Parameters.Length) +
            unit.Relocations.Length * UnitStoreFormat.RelocationSize + UnitStoreFormat.Align8(keys.Length * 4);
        foreach (var (block, count) in unit.BlockEmissionCounts)
        {
            BinaryPrimitives.WriteInt32LittleEndian(record[at..], block);
            BinaryPrimitives.WriteInt32LittleEndian(record[(at + 4)..], count);
            at += UnitStoreFormat.BlockSize;
        }
        if (unit.ExceptionGroups is { } groups)
        {
            foreach (var group in groups)
            {
                foreach (var word in new[] { group.Id, group.Parent, group.TryOffset, group.TryLength, group.Regions.Length })
                {
                    BinaryPrimitives.WriteInt32LittleEndian(record[at..], word);
                    at += 4;
                }
                foreach (var region in group.Regions)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(record[at..], region);
                    at += 4;
                }
            }
        }
        at = (int)size - UnitStoreFormat.Align8(rootMapKeys.Length * 4);
        for (var index = 0; index < rootMapKeys.Length; index++, at += 4)
        {
            BinaryPrimitives.WriteInt32LittleEndian(record[at..],
                QueryId(rootMapDependencies[index], rootMapKeys[index]));
        }
        _records.Advance((int)size);
        _identities.Add(unit.Identity);
        _entries.Add((UnitStoreFormat.HashOf(unit.Identity), offset));
        return true;
    }

    // The segment's bytes in one sequential write.
    internal void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var strings = (long)UnitStoreFormat.SegmentHeaderSize;
        var blob = Align8(strings + _strings.WrittenCount * 2L);
        var queries = Align8(blob + _blob.WrittenCount);
        var records = queries + _queries.WrittenCount;
        Span<byte> header = stackalloc byte[UnitStoreFormat.SegmentHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, UnitStoreFormat.Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], UnitStoreFormat.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], UnitStoreFormat.SegmentKind);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], _queries.WrittenCount / UnitStoreFormat.QuerySize);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], strings);
        BinaryPrimitives.WriteInt64LittleEndian(header[24..], _strings.WrittenCount);
        BinaryPrimitives.WriteInt64LittleEndian(header[32..], blob);
        BinaryPrimitives.WriteInt64LittleEndian(header[40..], _blob.WrittenCount);
        BinaryPrimitives.WriteInt64LittleEndian(header[48..], queries);
        BinaryPrimitives.WriteInt64LittleEndian(header[56..], records);
        Span<byte> padding = stackalloc byte[8];
        padding.Clear();
        destination.Write(header);
        destination.Write(MemoryMarshal.AsBytes(_strings.WrittenSpan));
        destination.Write(padding[..(int)(blob - strings - _strings.WrittenCount * 2L)]);
        destination.Write(_blob.WrittenSpan);
        destination.Write(padding[..(int)(queries - blob - _blob.WrittenCount)]);
        destination.Write(_queries.WrittenSpan);
        destination.Write(_records.WrittenSpan);
    }

    // Each query's interned key; false when a query this segment holds answered differently.
    private bool TryKeys(ImmutableArray<UnitDependency> dependencies, out long[] keys)
    {
        keys = new long[dependencies.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            var dependency = dependencies[index];
            keys[index] = Intern(dependency.Key);
            if (_queryIds.TryGetValue((dependency.Kind, keys[index]), out var known) &&
                known.Answer != dependency.Answer)
            {
                return false;
            }
        }
        return true;
    }

    private static int GroupWords(ImmutableArray<UnitExceptionGroup>? groups)
    {
        if (groups is not { } list) return -1;
        var words = 0;
        foreach (var group in list) words += 5 + group.Regions.Length;
        return words;
    }

    // A query's id in the segment, writing it, with its answer's value, the first time.
    private int QueryId(UnitDependency dependency, long key)
    {
        var (kind, answer) = (dependency.Kind, dependency.Answer);
        if (_queryIds.TryGetValue((kind, key), out var known))
            return known.Id;
        var value = 0L;
        if (UnitAnswerCodec.Encode(kind, dependency.Key, dependency.Value) is { Length: > 0 } encoded)
        {
            value = UnitStoreFormat.Pack(_blob.WrittenCount, encoded.Length);
            _blob.Write(encoded);
        }
        var id = _queries.WrittenCount / UnitStoreFormat.QuerySize;
        var query = _queries.GetSpan(UnitStoreFormat.QuerySize)[..UnitStoreFormat.QuerySize];
        query.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(query, (ushort)kind);
        BinaryPrimitives.WriteInt64LittleEndian(query[8..], key);
        BinaryPrimitives.WriteUInt64LittleEndian(query[16..], answer.First);
        BinaryPrimitives.WriteUInt64LittleEndian(query[24..], answer.Second);
        BinaryPrimitives.WriteInt64LittleEndian(query[32..], value);
        _queries.Advance(UnitStoreFormat.QuerySize);
        _queryIds[(kind, key)] = (id, answer);
        return id;
    }

    private long Intern(string value)
    {
        if (_interned.TryGetValue(value, out var reference))
            return reference;
        reference = UnitStoreFormat.Pack(_strings.WrittenCount, value.Length);
        _strings.Write(value.AsSpan());
        _interned[value] = reference;
        return reference;
    }

    private static long Align8(long value) => (value + 7) & ~7L;

    private static void WriteRef(Span<byte> record, int at, long reference) =>
        BinaryPrimitives.WriteInt64LittleEndian(record[at..], reference);
}
