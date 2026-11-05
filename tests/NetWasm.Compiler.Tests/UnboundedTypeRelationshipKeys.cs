using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// Keys that never narrow a pair, for tests whose relationships ignore the type hierarchy.
internal sealed class UnboundedTypeRelationshipKeys : ITypeRelationshipKeyResolver
{
    public static UnboundedTypeRelationshipKeys Instance { get; } = new();

    public TypeRelationshipKeys Resolve(CliTypeIdentity type) =>
        new(type.CanonicalName, ImmutableHashSet<string>.Empty, Unbounded: true);
}
