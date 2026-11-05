using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// How a method's view of the instruction target answers its maps: by recording each lookup
/// against the compile's own maps, or by replaying the last compile's answers.
/// </summary>
internal interface IUnitTargetMaps
{
    // A map looked up by key: each lookup is one query.
    IReadOnlyDictionary<TKey, TValue> Answer<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> inner, UnitQueryKind kind,
        Func<TKey, string> keyText, UnitFingerprintAdder<TValue> addValue) where TKey : notnull;

    // A set tested for membership: each test is one query.
    IReadOnlySet<T> Answer<T>(IReadOnlySet<T> inner, UnitQueryKind kind, Func<T, string> keyText);

    // A map read as a whole: any read is one query.
    IReadOnlyDictionary<TKey, TValue> Answer<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> inner, UnitQueryKind kind,
        Func<IReadOnlyDictionary<TKey, TValue>, UnitFingerprint> answer) where TKey : notnull;

    // A list read as a whole: any read is one query.
    IReadOnlyList<T> Answer<T>(IReadOnlyList<T> inner, UnitQueryKind kind, Func<IReadOnlyList<T>, UnitFingerprint> answer);
}

internal sealed class RecordingTargetMaps(IUnitDependencyRecorder recorder) : IUnitTargetMaps
{
    public IReadOnlyDictionary<TKey, TValue> Answer<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> inner, UnitQueryKind kind,
        Func<TKey, string> keyText, UnitFingerprintAdder<TValue> addValue) where TKey : notnull =>
        new RecordingDictionary<TKey, TValue>(inner, recorder, kind, keyText, addValue);

    public IReadOnlySet<T> Answer<T>(IReadOnlySet<T> inner, UnitQueryKind kind, Func<T, string> keyText) =>
        new RecordingSet<T>(inner, recorder, kind, keyText);

    public IReadOnlyDictionary<TKey, TValue> Answer<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> inner, UnitQueryKind kind,
        Func<IReadOnlyDictionary<TKey, TValue>, UnitFingerprint> answer) where TKey : notnull =>
        new WholeRecordingDictionary<TKey, TValue>(inner, recorder, kind, answer);

    public IReadOnlyList<T> Answer<T>(IReadOnlyList<T> inner, UnitQueryKind kind,
        Func<IReadOnlyList<T>, UnitFingerprint> answer) =>
        new WholeRecordingList<T>(inner, recorder, kind, answer);
}

/// <summary>Answers the target's maps from the last compile's answers; the plans' own maps are empty.</summary>
internal sealed class ReplayTargetMaps(UnitAnswers answers) : IUnitTargetMaps
{
    public IReadOnlyDictionary<TKey, TValue> Answer<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> inner, UnitQueryKind kind,
        Func<TKey, string> keyText, UnitFingerprintAdder<TValue> addValue) where TKey : notnull =>
        new ReplayDictionary<TKey, TValue>(answers, kind, keyText);

    public IReadOnlySet<T> Answer<T>(IReadOnlySet<T> inner, UnitQueryKind kind, Func<T, string> keyText) =>
        new ReplaySet<T>(answers, kind, keyText);

    public IReadOnlyDictionary<TKey, TValue> Answer<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> inner, UnitQueryKind kind,
        Func<IReadOnlyDictionary<TKey, TValue>, UnitFingerprint> answer) where TKey : notnull =>
        new WholeReplayDictionary<TKey, TValue>(answers, kind);

    public IReadOnlyList<T> Answer<T>(IReadOnlyList<T> inner, UnitQueryKind kind,
        Func<IReadOnlyList<T>, UnitFingerprint> answer) =>
        new WholeReplayList<T>(answers, kind);
}

internal sealed class ReplayDictionary<TKey, TValue>(UnitAnswers answers, UnitQueryKind kind, Func<TKey, string> keyText)
    : IReadOnlyDictionary<TKey, TValue> where TKey : notnull
{
    public TValue this[TKey key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(keyText(key));

    public IEnumerable<TKey> Keys => throw Unanswerable();

    public IEnumerable<TValue> Values => throw Unanswerable();

    public int Count => throw Unanswerable();

    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var lookup = answers.Get<UnitLookup<TValue>>(kind, keyText(key));
        value = lookup.Value!;
        return lookup.Found;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => throw Unanswerable();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private UnitReplayMissException Unanswerable() => new($"{kind} entries can only be replayed by key.");
}

internal sealed class ReplaySet<T>(UnitAnswers answers, UnitQueryKind kind, Func<T, string> keyText) : IReadOnlySet<T>
{
    public bool Contains(T item) => answers.Get<bool>(kind, keyText(item));

    public int Count => throw Unanswerable();
    public IEnumerator<T> GetEnumerator() => throw Unanswerable();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool IsProperSubsetOf(IEnumerable<T> other) => throw Unanswerable();
    public bool IsProperSupersetOf(IEnumerable<T> other) => throw Unanswerable();
    public bool IsSubsetOf(IEnumerable<T> other) => throw Unanswerable();
    public bool IsSupersetOf(IEnumerable<T> other) => throw Unanswerable();
    public bool Overlaps(IEnumerable<T> other) => throw Unanswerable();
    public bool SetEquals(IEnumerable<T> other) => throw Unanswerable();

    private UnitReplayMissException Unanswerable() => new($"{kind} membership can only be replayed by member.");
}

internal sealed class WholeReplayDictionary<TKey, TValue>(UnitAnswers answers, UnitQueryKind kind)
    : IReadOnlyDictionary<TKey, TValue> where TKey : notnull
{
    private IReadOnlyDictionary<TKey, TValue> Read() => answers.Get<IReadOnlyDictionary<TKey, TValue>>(kind, "");

    public TValue this[TKey key] => Read()[key];
    public IEnumerable<TKey> Keys => Read().Keys;
    public IEnumerable<TValue> Values => Read().Values;
    public int Count => Read().Count;
    public bool ContainsKey(TKey key) => Read().ContainsKey(key);
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => Read().TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Read().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class WholeReplayList<T>(UnitAnswers answers, UnitQueryKind kind) : IReadOnlyList<T>
{
    private IReadOnlyList<T> Read() => answers.Get<IReadOnlyList<T>>(kind, "");

    public T this[int index] => Read()[index];
    public int Count => Read().Count;
    public IEnumerator<T> GetEnumerator() => Read().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
