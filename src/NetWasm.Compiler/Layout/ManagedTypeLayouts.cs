using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed record ManagedTypeLayouts(
    ImmutableDictionary<EntityKey, ObjectLayout> Objects,
    ImmutableDictionary<CliTypeIdentity, ObjectLayout> ObjectIdentities,
    ImmutableDictionary<string, ObjectLayout> ObjectsByName,
    ImmutableDictionary<CliTypeIdentity, ObjectLayout> ConstructedObjects,
    ManagedValueLayoutState ValueLayoutState,
    WasmTargetLayout Target,
    int DelegateTargetOffset,
    int DelegateMethodIdOffset,
    int DelegateLeftOffset,
    int DelegateRightOffset);
