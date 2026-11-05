using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// The last compile's answers to every query its units asked, for a compile that replays
/// them instead of asking. Only queries of the units the last compile used are taken: each
/// was answered, or checked against its answer, by that compile. A query no such unit
/// asked, or whose answer is not kept, cannot be replayed (<see cref="UnitReplayMissException"/>).
/// </summary>
internal sealed class UnitAnswers
{
    private readonly UnitStore _store;
    private readonly IMethodRepository _methods;
    private readonly IFieldRepository _fields;
    private readonly Dictionary<(UnitQueryKind Kind, string Key), (int Segment, int Id)> _index;
    private readonly ConcurrentDictionary<(UnitQueryKind Kind, string Key), object> _values = new();
    private readonly IReadOnlyDictionary<(UnitQueryKind Kind, string Key), object> _overlay;
    private readonly IReadOnlySet<string> _owners;

    private UnitAnswers(UnitStore store, IMethodRepository methods, IFieldRepository fields,
        Dictionary<(UnitQueryKind Kind, string Key), (int Segment, int Id)> index,
        IReadOnlyDictionary<(UnitQueryKind Kind, string Key), object>? overlay = null, IReadOnlySet<string>? owners = null)
    {
        _store = store;
        _methods = methods;
        _fields = fields;
        _index = index;
        _overlay = overlay ?? new Dictionary<(UnitQueryKind Kind, string Key), object>();
        _owners = owners ?? new HashSet<string>(StringComparer.Ordinal);
    }

    // These answers with the given ones in front: an edited method's own facts. Every site
    // of an owner (a method edited since) is answered by them alone, found or not; the last
    // compile's answers for its sites belong to its old body.
    internal UnitAnswers Overlaid(IReadOnlyDictionary<(UnitQueryKind Kind, string Key), object> overlay,
        IReadOnlySet<string> owners) => new(_store, _methods, _fields, _index, overlay, owners);

    internal int Count => _index.Count;

    internal static UnitAnswers Over(UnitStore store, IMethodRepository methods, IFieldRepository fields)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(fields);
        var seen = new HashSet<(int Segment, int Id)>();
        var index = new Dictionary<(UnitQueryKind Kind, string Key), (int Segment, int Id)>();
        void Add(int segmentOrdinal, UnitSegment segment, int id)
        {
            if (!seen.Add((segmentOrdinal, id))) return;
            var query = segment.Query(id);
            index.TryAdd((query.Kind, query.Key.ToString()), (segmentOrdinal, id));
        }
        foreach (var unit in store.LiveUnits())
        {
            var segment = store.Segments[unit.Segment]!;
            var record = segment.Record(unit.Offset);
            for (var query = 0; query < record.QueryCount; query++) Add(unit.Segment, segment, record.QueryId(query));
            for (var query = 0; query < record.RootMapQueryCount; query++)
                Add(unit.Segment, segment, record.RootMapQueryId(query));
        }
        return new(store, methods, fields, index);
    }

    // The answer to a query, as its provider returned it.
    internal T Get<T>(UnitQueryKind kind, string key)
    {
        if (_overlay.TryGetValue((kind, key), out var given))
        {
            return given is T answer
                ? answer
                : throw new UnitReplayMissException($"{kind} '{key}' was given as {given.GetType().Name}, not {typeof(T).Name}.");
        }
        if (kind is UnitQueryKind.ManagedCallSite or UnitQueryKind.DispatchCallSite or UnitQueryKind.TypeTestSite &&
            key.LastIndexOf('@') is var at and > 0 && _owners.Contains(key[..at]))
        {
            // A lookup of a site the edited body does not have: not found.
            return default(T) is { } absent
                ? absent
                : throw new UnitReplayMissException($"{kind} '{key}' cannot be answered as absent.");
        }
        var value = _values.GetOrAdd((kind, key), static (query, answers) => answers.Decode(query.Kind, query.Key), this);
        return value is T typed
            ? typed
            : throw new UnitReplayMissException($"{kind} '{key}' was kept as {value.GetType().Name}, not {typeof(T).Name}.");
    }

    private object Decode(UnitQueryKind kind, string key)
    {
        if (!_index.TryGetValue((kind, key), out var at))
            throw new UnitReplayMissException($"No unit of the last compile asked {kind} '{key}'.");
        var query = _store.Segments[at.Segment]!.Query(at.Id);
        if (query.Value.IsEmpty)
            throw new UnitReplayMissException($"The last compile kept no answer to {kind} '{key}'.");
        return UnitAnswerCodec.Decode(kind, key, query.Value, _methods, _fields);
    }
}

/// <summary>A replayed compile asked something the last compile's answers do not hold.</summary>
internal sealed class UnitReplayMissException : Exception
{
    public UnitReplayMissException()
    {
    }

    public UnitReplayMissException(string message) : base(message)
    {
    }

    public UnitReplayMissException(string message, Exception inner) : base(message, inner)
    {
    }
}
