using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedValueLayoutState
{
    public Dictionary<EntityKey, FieldLayout> Fields { get; } = [];
    public Dictionary<string, FieldLayout> ConstructedFields { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<CliTypeIdentity, ValueLayout> Values { get; } = [];

    public HashSet<CliTypeIdentity> CompletedMetadataLayouts { get; } = [];

    public ManagedValueLayoutState() { }

    public ManagedValueLayoutState(ManagedValueLayoutState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var pair in source.Fields) Fields.Add(pair.Key, pair.Value);
        foreach (var pair in source.ConstructedFields)
            ConstructedFields.Add(pair.Key, pair.Value);
        foreach (var pair in source.Values) Values.Add(pair.Key, pair.Value);
        CompletedMetadataLayouts.UnionWith(source.CompletedMetadataLayouts);
    }
}
