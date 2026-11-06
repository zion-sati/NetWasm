using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// A method's root map as its unit keeps it: little-endian words, with every map in the
/// order the analysis produced it, so a restored map enumerates as the analysed one did.
/// </summary>
internal static class UnitRootMapFormat
{
    public static ImmutableArray<byte> Write(MethodRootMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var writer = new ArrayBufferWriter<byte>(256);
        Word(writer, map.Slots.Count);
        foreach (var (source, slot) in map.Slots)
        {
            Root(writer, source);
            Word(writer, slot);
        }
        Word(writer, map.Safepoints.Count);
        foreach (var (offset, safepoint) in map.Safepoints)
        {
            Word(writer, offset);
            Word(writer, safepoint.IlOffset);
            Roots(writer, safepoint.Roots);
            Roots(writer, safepoint.ConstructorCallRoots);
        }
        Word(writer, map.AnchorPlan.Anchors.Length);
        foreach (var anchor in map.AnchorPlan.Anchors)
        {
            Word(writer, (int)anchor.Location.Kind);
            Word(writer, anchor.Location.Index);
            var name = anchor.BackingType.CanonicalName;
            Word(writer, name.Length);
            writer.Write(MemoryMarshal.AsBytes(name.AsSpan()));
        }
        Word(writer, map.AnchorPlan.Assignments.Count);
        foreach (var (offset, assignments) in map.AnchorPlan.Assignments)
        {
            Word(writer, offset);
            Word(writer, assignments.Length);
            foreach (var assignment in assignments)
            {
                switch (assignment)
                {
                    case StackStorageAnchorAssignment.Capture capture:
                        Word(writer, 0);
                        Word(writer, capture.Anchor);
                        Word(writer, capture.EvaluationStackSlot);
                        break;
                    case StackStorageAnchorAssignment.Copy copy:
                        Word(writer, 1);
                        Word(writer, copy.Anchor);
                        Word(writer, copy.Source);
                        break;
                    case StackStorageAnchorAssignment.Clear clear:
                        Word(writer, 2);
                        Word(writer, clear.Anchor);
                        Word(writer, 0);
                        break;
                    default:
                        throw new NotSupportedException($"Unknown anchor assignment {assignment.GetType().Name}.");
                }
            }
        }
        return [.. writer.WrittenSpan];
    }

    // The map written for the method, or null when an anchor's backing type cannot be named.
    public static MethodRootMap? Read(ReadOnlySpan<byte> bytes, EntityKey method, Func<string, CliTypeIdentity?> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var reader = new Reader(bytes);
        var slotCount = reader.Count();
        var slots = ImmutableDictionary.CreateBuilder<RootSource, int>();
        for (var index = 0; index < slotCount; index++)
        {
            var source = reader.Root();
            slots.Add(source, reader.Word());
        }
        var safepointCount = reader.Count();
        var safepoints = ImmutableDictionary.CreateBuilder<int, SafepointRootMap>();
        for (var index = 0; index < safepointCount; index++)
        {
            var offset = reader.Word();
            var ilOffset = reader.Word();
            var roots = reader.Roots();
            safepoints.Add(offset, new(ilOffset, roots, reader.Roots()));
        }
        var anchorCount = reader.Count();
        var anchors = ImmutableArray.CreateBuilder<StackStorageAnchor>(anchorCount);
        for (var index = 0; index < anchorCount; index++)
        {
            var location = new StackStorageLocation((StackStorageLocationKind)reader.Word(), reader.Word());
            if (types(reader.Text()) is not { } backing) return null;
            anchors.Add(new(location, backing));
        }
        var assignmentCount = reader.Count();
        var assignments = ImmutableDictionary.CreateBuilder<int, ImmutableArray<StackStorageAnchorAssignment>>();
        for (var index = 0; index < assignmentCount; index++)
        {
            var offset = reader.Word();
            var count = reader.Count();
            var list = ImmutableArray.CreateBuilder<StackStorageAnchorAssignment>(count);
            for (var item = 0; item < count; item++)
            {
                var kind = reader.Word();
                var anchor = reader.Word();
                var value = reader.Word();
                list.Add(kind switch
                {
                    0 => new StackStorageAnchorAssignment.Capture(anchor, value),
                    1 => new StackStorageAnchorAssignment.Copy(anchor, value),
                    2 => new StackStorageAnchorAssignment.Clear(anchor),
                    _ => throw new InvalidDataException("A unit's root map is corrupt."),
                });
            }
            assignments.Add(offset, list.MoveToImmutable());
        }
        if (!reader.AtEnd) throw new InvalidDataException("A unit's root map is corrupt.");
        return new MethodRootMap(method, slots.ToImmutable(), safepoints.ToImmutable())
        {
            AnchorPlan = anchors.Count == 0 && assignments.Count == 0
                ? StackStorageAnchorPlan.Empty
                : new(anchors.MoveToImmutable(), assignments.ToImmutable()),
        };
    }

    private static void Word(ArrayBufferWriter<byte> writer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), value);
        writer.Advance(4);
    }

    private static void Root(ArrayBufferWriter<byte> writer, RootSource source)
    {
        Word(writer, (int)source.Kind);
        Word(writer, source.Index);
        Word(writer, source.ByteOffset);
    }

    private static void Roots(ArrayBufferWriter<byte> writer, ImmutableArray<RootSource> roots)
    {
        Word(writer, roots.Length);
        foreach (var root in roots) Root(writer, root);
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _at;

        public readonly bool AtEnd => _at == _bytes.Length;

        public int Word()
        {
            if (_at + 4 > _bytes.Length) throw new InvalidDataException("A unit's root map is corrupt.");
            var value = BinaryPrimitives.ReadInt32LittleEndian(_bytes[_at..]);
            _at += 4;
            return value;
        }

        public int Count()
        {
            var count = Word();
            return count >= 0 && count <= (_bytes.Length - _at) / 4 + 1
                ? count
                : throw new InvalidDataException("A unit's root map is corrupt.");
        }

        public RootSource Root() => new((RootSourceKind)Word(), Word(), Word());

        public ImmutableArray<RootSource> Roots()
        {
            var count = Count();
            var roots = ImmutableArray.CreateBuilder<RootSource>(count);
            for (var index = 0; index < count; index++) roots.Add(Root());
            return roots.MoveToImmutable();
        }

        // A name's length counts its UTF-16 characters, two bytes each.
        public string Text()
        {
            var length = Word();
            if (length < 0 || _at + length * 2L > _bytes.Length) throw new InvalidDataException("A unit's root map is corrupt.");
            var text = System.Text.Encoding.Unicode.GetString(_bytes.Slice(_at, length * 2));
            _at += length * 2;
            return text;
        }
    }
}

/// <summary>
/// One root-map worker's side of the compile's units. Its providers record the
/// whole-program facts a method's root-map analysis reads (value layouts, dispatch sites,
/// which callees may allocate), kept for the method's unit. A method whose unit kept its
/// root map takes that map instead of analysing, when the unit's input matches and every
/// fact it recorded still holds; under verification it is analysed too and must match.
/// </summary>
internal sealed class UnitRootMapRecorder
{
    private readonly CompiledUnitState _state;
    private readonly IFieldRepository _fieldDefinitions;
    private readonly UnitDependencyLog _log = new();

    public UnitRootMapRecorder(
        CompiledUnitState state,
        IValueLayoutProvider values,
        IEntryLookup<string, DispatchCallSiteModel> dispatchCallSites,
        IMembership<EntityKey> allocatingMethods,
        IMembership<string> allocatingConstructedMethods,
        IFieldRepository fieldDefinitions)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _fieldDefinitions = fieldDefinitions ?? throw new ArgumentNullException(nameof(fieldDefinitions));
        Values = new RecordingValueLayoutProvider(values, _log);
        DispatchCallSites = new RecordingLookup<string, DispatchCallSiteModel>(
            dispatchCallSites, _log, UnitQueryKind.DispatchCallSite,
            static key => key, static (ref answer, site) => answer.Add(site));
        AllocatingMethods = new RecordingMembership<EntityKey>(
            allocatingMethods, _log, UnitQueryKind.AllocatingMethod, UnitQueryKeys.Of);
        AllocatingConstructedMethods = new RecordingMembership<string>(
            allocatingConstructedMethods, _log, UnitQueryKind.AllocatingConstructedMethod, static key => key);
    }

    public IValueLayoutProvider Values { get; }
    public IEntryLookup<string, DispatchCallSiteModel> DispatchCallSites { get; }
    public IMembership<EntityKey> AllocatingMethods { get; }
    public IMembership<string> AllocatingConstructedMethods { get; }

    // The method's root map: its unit's where that holds, otherwise analysed with the facts
    // it reads recorded for its unit.
    internal MethodRootMap Map(ManagedMethodBody method, Func<MethodRootMap> analyze)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(analyze);
        var identity = method.Method.CanonicalName;
        var restored = Restore(method, identity);
        if (restored is not null && !_state.Verifies)
            return restored;
        _log.Begin();
        MethodRootMap analysed;
        try
        {
            analysed = analyze();
        }
        finally
        {
            _state.RootMapDependencies[identity] = _log.End();
        }
        if (restored is null)
            return analysed;
        var expected = new UnitFingerprintBuilder();
        expected.Add(analysed);
        var actual = new UnitFingerprintBuilder();
        actual.Add(restored);
        return expected.Finish() == actual.Finish()
            ? restored
            : throw new InvalidOperationException(
                $"The compiled unit of {identity} restored a root map that differs from analysis.");
    }

    private MethodRootMap? Restore(ManagedMethodBody method, string identity)
    {
        var store = _state.Store;
        // A store another process replaced or damaged costs reuse, never the compile.
        try
        {
            if (store.Find(identity) is not { } location)
                return null;
            var segment = store.Segments[location.Segment]!;
            var unit = segment.Record(location.Offset);
            if (!unit.HasRootMap)
                return null;
            UnitQueryArguments? arguments = null;
            CliTypeIdentity? Type(string name)
            {
                arguments ??= UnitQueryArguments.Of(method);
                return UnitQueryReplayer.TryType(name, arguments, _fieldDefinitions, out var type) ? type : null;
            }
            var map = UnitRootMapFormat.Read(unit.RootMapBytes, method.Method.Definition.Key, Type);
            if (map is null || _state.InputOf(identity, map) is not { } input || input != unit.Input)
                return null;
            for (var index = 0; index < unit.RootMapQueryCount; index++)
            {
                var id = unit.RootMapQueryId(index);
                ref var slot = ref segment.QueryState(id);
                var status = Volatile.Read(ref slot);
                if (status == UnitSegment.Unknown)
                {
                    status = Check(segment.Query(id), method, ref arguments);
                    Volatile.Write(ref slot, status);
                }
                if (status != UnitSegment.Valid)
                    return null;
            }
            _state.RootMapSources[identity] = location;
            Interlocked.Increment(ref _state.RestoredRootMaps);
            return map;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    // Whether a recorded fact holds in this compile, asked through the recording providers
    // that recorded it.
    private int Check(UnitQuery query, ManagedMethodBody method, ref UnitQueryArguments? arguments)
    {
        var key = query.Key.ToString();
        bool asked;
        _log.Begin();
        try
        {
            asked = Ask(query.Kind, key, method, ref arguments);
        }
        catch (CompilerException)
        {
            _log.End();
            return UnitSegment.Failed;
        }
        catch (KeyNotFoundException)
        {
            _log.End();
            return UnitSegment.Failed;
        }
        var answers = _log.End();
        if (!asked)
            return UnitSegment.Unbuildable;
        foreach (var answer in answers)
        {
            if (answer.Kind == query.Kind && string.Equals(answer.Key, key, StringComparison.Ordinal))
                return answer.Answer == query.Answer ? UnitSegment.Valid : UnitSegment.Changed;
        }
        return UnitSegment.Unbuildable;
    }

    private bool Ask(UnitQueryKind kind, string key, ManagedMethodBody method, ref UnitQueryArguments? arguments)
    {
        switch (kind)
        {
            case UnitQueryKind.ValueLayout:
                arguments ??= UnitQueryArguments.Of(method);
                if (!UnitQueryReplayer.TryType(key, arguments, _fieldDefinitions, out var type)) return false;
                _ = Values.GetValueLayout(type);
                return true;
            case UnitQueryKind.DispatchCallSite:
                _ = DispatchCallSites.TryGetValue(key, out _);
                return true;
            case UnitQueryKind.AllocatingMethod:
                if (!UnitQueryReplayer.TryParse(key, out var definition)) return false;
                _ = AllocatingMethods.Contains(definition);
                return true;
            case UnitQueryKind.AllocatingConstructedMethod:
                _ = AllocatingConstructedMethods.Contains(key);
                return true;
            default:
                return false;
        }
    }
}
