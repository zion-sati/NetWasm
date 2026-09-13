using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedTypeLayoutBuildState(ManagedValueLayoutState valueLayouts)
{
    public Dictionary<EntityKey, ObjectLayout> Objects { get; } = [];
    public Dictionary<CliTypeIdentity, ObjectLayout> ObjectIdentities { get; } = [];
    public Dictionary<string, ObjectLayout> ObjectsByName { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<CliTypeIdentity, ObjectLayout> ConstructedObjects { get; } = [];
    public ManagedValueLayoutState ValueLayouts { get; } = valueLayouts ??
        throw new ArgumentNullException(nameof(valueLayouts));
    public int NextTypeId { get; set; } = 1;
}
