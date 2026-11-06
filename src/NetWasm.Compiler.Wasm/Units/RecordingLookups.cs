using System;
using System.Diagnostics.CodeAnalysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// Entries a unit's emission or analysis looks up, recording each key it asks for and
/// whether and what it found.
/// </summary>
internal sealed class RecordingLookup<TKey, TValue>(
    IEntryLookup<TKey, TValue> inner,
    IUnitDependencyRecorder recorder,
    UnitQueryKind kind,
    Func<TKey, string> keyText,
    UnitFingerprintAdder<TValue> addValue) : IEntryLookup<TKey, TValue>
    where TKey : notnull
{
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var found = inner.TryGetValue(key, out value);
        var answer = new UnitFingerprintBuilder();
        answer.Add(found);
        if (found) addValue(ref answer, value!);
        recorder.Record(kind, keyText(key), answer.Finish(), new UnitLookup<TValue>(found, value));
        return found;
    }
}

/// <summary>Items a unit tests membership of, recording each item it asks about and the answer.</summary>
internal sealed class RecordingMembership<T>(
    IMembership<T> inner,
    IUnitDependencyRecorder recorder,
    UnitQueryKind kind,
    Func<T, string> keyText) : IMembership<T>
{
    public bool Contains(T item)
    {
        var found = inner.Contains(item);
        var answer = new UnitFingerprintBuilder();
        answer.Add(found);
        recorder.Record(kind, keyText(item), answer.Finish(), found);
        return found;
    }
}

/// <summary>A collection a unit's emission reads as a whole: each read records one query.</summary>
internal sealed class RecordingWholeRead<T>(
    IWholeRead<T> inner,
    IUnitDependencyRecorder recorder,
    UnitQueryKind kind,
    Func<T, UnitFingerprint> answer) : IWholeRead<T>
{
    public T Read()
    {
        var value = inner.Read();
        recorder.Record(kind, "", answer(value), value);
        return value;
    }
}
