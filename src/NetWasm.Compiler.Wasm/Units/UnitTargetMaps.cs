using System;
using System.Diagnostics.CodeAnalysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// How a method's view of the instruction target answers its maps: by recording each lookup
/// against the compile's own maps, or by replaying the last compile's answers.
/// </summary>
internal interface IUnitTargetMaps
{
    // Entries looked up by key: each lookup is one query.
    IEntryLookup<TKey, TValue> Answer<TKey, TValue>(IEntryLookup<TKey, TValue> inner, UnitQueryKind kind,
        Func<TKey, string> keyText, UnitFingerprintAdder<TValue> addValue) where TKey : notnull;

    // Items tested for membership: each test is one query.
    IMembership<T> Answer<T>(IMembership<T> inner, UnitQueryKind kind, Func<T, string> keyText);

    // A collection read as a whole: any read is one query.
    IWholeRead<T> Answer<T>(IWholeRead<T> inner, UnitQueryKind kind, Func<T, UnitFingerprint> answer);
}

internal sealed class RecordingTargetMaps(IUnitDependencyRecorder recorder) : IUnitTargetMaps
{
    public IEntryLookup<TKey, TValue> Answer<TKey, TValue>(IEntryLookup<TKey, TValue> inner, UnitQueryKind kind,
        Func<TKey, string> keyText, UnitFingerprintAdder<TValue> addValue) where TKey : notnull =>
        new RecordingLookup<TKey, TValue>(inner, recorder, kind, keyText, addValue);

    public IMembership<T> Answer<T>(IMembership<T> inner, UnitQueryKind kind, Func<T, string> keyText) =>
        new RecordingMembership<T>(inner, recorder, kind, keyText);

    public IWholeRead<T> Answer<T>(IWholeRead<T> inner, UnitQueryKind kind, Func<T, UnitFingerprint> answer) =>
        new RecordingWholeRead<T>(inner, recorder, kind, answer);
}

/// <summary>Answers the target's maps from the last compile's answers; the plans' own maps are empty.</summary>
internal sealed class ReplayTargetMaps(UnitAnswers answers) : IUnitTargetMaps
{
    public IEntryLookup<TKey, TValue> Answer<TKey, TValue>(IEntryLookup<TKey, TValue> inner, UnitQueryKind kind,
        Func<TKey, string> keyText, UnitFingerprintAdder<TValue> addValue) where TKey : notnull =>
        new ReplayLookup<TKey, TValue>(answers, kind, keyText);

    public IMembership<T> Answer<T>(IMembership<T> inner, UnitQueryKind kind, Func<T, string> keyText) =>
        new ReplayMembership<T>(answers, kind, keyText);

    public IWholeRead<T> Answer<T>(IWholeRead<T> inner, UnitQueryKind kind, Func<T, UnitFingerprint> answer) =>
        new ReplayWholeRead<T>(answers, kind);
}

/// <summary>Entries looked up by key, answered from the last compile's lookups.</summary>
internal sealed class ReplayLookup<TKey, TValue>(UnitAnswers answers, UnitQueryKind kind, Func<TKey, string> keyText)
    : IEntryLookup<TKey, TValue> where TKey : notnull
{
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var lookup = answers.Get<UnitLookup<TValue>>(kind, keyText(key));
        value = lookup.Value!;
        return lookup.Found;
    }
}

/// <summary>Items tested for membership, answered from the last compile's tests.</summary>
internal sealed class ReplayMembership<T>(UnitAnswers answers, UnitQueryKind kind, Func<T, string> keyText) : IMembership<T>
{
    public bool Contains(T item) => answers.Get<bool>(kind, keyText(item));
}

/// <summary>A collection read as a whole, answered from the last compile's read.</summary>
internal sealed class ReplayWholeRead<T>(UnitAnswers answers, UnitQueryKind kind) : IWholeRead<T>
{
    public T Read() => answers.Get<T>(kind, "");
}
