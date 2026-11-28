// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow;

// Set operations on blocks that cost the smaller set. ImmutableHashSet's Intersect and IsSubsetOf walk their argument,
// so asking them of a loop's blocks against the method's allowed blocks, once per loop, took the loops times the
// method's blocks.
internal static class BlockSets
{
    // The blocks that are also in `other`, each looked up there; the set itself when all of them are.
    internal static ImmutableHashSet<int> Within(this ImmutableHashSet<int> blocks, ImmutableHashSet<int> other) =>
        blocks.All(other.Contains) ? blocks : blocks.Where(other.Contains).ToImmutableHashSet();
}
