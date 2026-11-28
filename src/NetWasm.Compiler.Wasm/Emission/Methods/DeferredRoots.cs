// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// The roots a safepoint leaves unpublished: each of its deferrable roots (a point that collects only while what it throws
// travels to a handler here, which does not read them) whose slot is known to hold null there. The slot stays null, so a
// collection on the way to the handler keeps nothing they reference. The clear elisions record such a slot as known null
// at the point, so every planner and the publication itself agree on the set from those elisions.
internal static class DeferredRoots
{
    public static ImmutableHashSet<RootSource> Of(MethodRootMap roots, SafepointRootMap point, IReadOnlySet<int>? knownZero)
    {
        if (point.Deferrable.IsEmpty || knownZero is null || knownZero.Count == 0) return [];
        return point.Deferrable
            .Where(source => point.Roots.Contains(source) && roots.Slots.TryGetValue(source, out var slot) &&
                knownZero.Contains(slot))
            .ToImmutableHashSet();
    }
}
