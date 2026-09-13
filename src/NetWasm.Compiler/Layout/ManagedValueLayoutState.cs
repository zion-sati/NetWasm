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
}
