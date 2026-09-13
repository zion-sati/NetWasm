using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

public sealed record ReachabilityRoots(
    ImmutableArray<EntityKey> Types,
    ImmutableArray<EntityKey> Fields,
    ImmutableArray<EntityKey> Methods)
{
    public static ReachabilityRoots Empty { get; } = new([], [], []);
}
