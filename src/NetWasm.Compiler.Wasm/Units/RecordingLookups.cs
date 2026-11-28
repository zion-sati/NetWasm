// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
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
    // A key's answer is the same at every query, so it is fingerprinted once: the fingerprint grows with the value,
    // and a method asks for some keys at every instruction (its source locations, for one).
    private readonly Dictionary<TKey, (string Key, UnitLookup<TValue> Lookup, UnitFingerprint Answer)> _answers = [];

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_answers.TryGetValue(key, out var answered))
        {
            var found = inner.TryGetValue(key, out var entry);
            var answer = new UnitFingerprintBuilder();
            answer.Add(found);
            if (found) addValue(ref answer, entry!);
            answered = (keyText(key), new UnitLookup<TValue>(found, entry), answer.Finish());
            _answers.Add(key, answered);
        }
        recorder.Record(kind, answered.Key, answered.Answer, answered.Lookup);
        value = answered.Lookup.Value!;
        return answered.Lookup.Found;
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
    // The collection is the same at every read, so it is fingerprinted once.
    private (T Value, UnitFingerprint Answer)? _read;

    public T Read()
    {
        var read = _read ??= Fingerprinted(inner.Read());
        recorder.Record(kind, "", read.Answer, read.Value);
        return read.Value;
    }

    private (T, UnitFingerprint) Fingerprinted(T value) => (value, answer(value));
}
