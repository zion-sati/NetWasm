using System.Collections.Immutable;

namespace NetWasm.Wit.Bindings;

public sealed record WitBindingSectionSource(
    string Source,
    ImmutableArray<int> LiftTypes,
    ImmutableArray<int> LowerTypes);
