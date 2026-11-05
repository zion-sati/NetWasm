using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// A read-only map a unit's emission looks entries up in, recording each key it asks for
/// and whether and what it found. Enumerating it would make the unit depend on every
/// entry, so that is refused; a member that needs the whole set is recorded as one query.
/// </summary>
internal sealed class RecordingDictionary<TKey, TValue>(
    IReadOnlyDictionary<TKey, TValue> inner,
    IUnitDependencyRecorder recorder,
    UnitQueryKind kind,
    Func<TKey, string> keyText,
    UnitFingerprintAdder<TValue> addValue) : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    public TValue this[TKey key] => TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException(keyText(key));

    public IEnumerable<TKey> Keys => throw Unrecordable();

    public IEnumerable<TValue> Values => throw Unrecordable();

    public int Count => throw Unrecordable();

    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var found = inner.TryGetValue(key, out value);
        var answer = new UnitFingerprintBuilder();
        answer.Add(found);
        if (found) addValue(ref answer, value!);
        recorder.Record(kind, keyText(key), answer.Finish(), new UnitLookup<TValue>(found, value));
        return found;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => throw Unrecordable();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private NotSupportedException Unrecordable() =>
        new($"A unit's emission may only look up {kind} entries by key.");
}
