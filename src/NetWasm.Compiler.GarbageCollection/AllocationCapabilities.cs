using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record AllocationCapabilities(
    ImmutableHashSet<EntityKey> Methods,
    ImmutableHashSet<string> ConstructedMethods);
