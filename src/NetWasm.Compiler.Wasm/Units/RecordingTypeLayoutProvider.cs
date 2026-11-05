using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>Answers type layout queries and records each one as a unit dependency.</summary>
internal sealed class RecordingTypeLayoutProvider(
    ITypeLayoutProvider inner,
    IUnitDependencyRecorder recorder) : ITypeLayoutProvider
{
    private readonly ITypeLayoutProvider _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IUnitDependencyRecorder _recorder = recorder ??
        throw new ArgumentNullException(nameof(recorder));

    public int StringTypeId => Recorded(UnitQueryKind.StringTypeId, _inner.StringTypeId);

    public int TypeTypeId => Recorded(UnitQueryKind.TypeTypeId, _inner.TypeTypeId);

    public ObjectLayout GetObjectLayout(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var layout = _inner.GetObjectLayout(type);
        var answer = new UnitFingerprintBuilder();
        answer.Add(layout);
        _recorder.Record(UnitQueryKind.ObjectLayout, type.CanonicalName, answer.Finish(), layout);
        return layout;
    }

    public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
    {
        ArgumentNullException.ThrowIfNull(type);
        var found = _inner.GetObjectLayout(type, out layout);
        var answer = new UnitFingerprintBuilder();
        answer.Add(found);
        if (found) answer.Add(layout);
        _recorder.Record(UnitQueryKind.ObjectLayoutLookup, type.CanonicalName, answer.Finish(),
            new UnitLookup<ObjectLayout>(found, layout));
        return found;
    }

    public ObjectLayout GetObjectLayout(EntityKey type)
    {
        var layout = _inner.GetObjectLayout(type);
        var answer = new UnitFingerprintBuilder();
        answer.Add(layout);
        _recorder.Record(UnitQueryKind.ObjectLayoutOfDefinition, UnitQueryKeys.Of(type), answer.Finish(), layout);
        return layout;
    }

    private int Recorded(UnitQueryKind kind, int value)
    {
        var answer = new UnitFingerprintBuilder();
        answer.Add(value);
        _recorder.Record(kind, string.Empty, answer.Finish(), value);
        return value;
    }
}

/// <summary>Stable text keys for recorded query arguments.</summary>
internal static class UnitQueryKeys
{
    // A definition's metadata token shifts when its assembly gains a member; the key then
    // names another definition, whose answer differs, so the unit is not reused.
    public static string Of(EntityKey key) => $"{key.Assembly.Name}:0x{key.MetadataToken:x8}";
}
