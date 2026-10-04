using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

public sealed record ReachabilityRoots(
    ImmutableArray<EntityKey> Types,
    ImmutableArray<EntityKey> Fields,
    ImmutableArray<EntityKey> Methods)
{
    // Unlike retention-only Methods, these roots are entered through a generated
    // call boundary and therefore participate in static-call initialization.
    public ImmutableArray<EntityKey> InvokedMethods { get; init; } = [];

    public static ReachabilityRoots Empty { get; } = new([], [], []);
}
