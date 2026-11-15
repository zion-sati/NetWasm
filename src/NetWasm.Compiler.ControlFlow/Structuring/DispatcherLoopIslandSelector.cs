// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class DispatcherLoopIslandSelector : IDispatcherLoopIslandSelector
{
    public ImmutableHashSet<int> Select(
        ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        ImmutableHashSet<int> dispatcherOwnedBlocks)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(dispatcherOwnedBlocks);

        if (entry is not int entryBlock ||
            !dispatcherOwnedBlocks.Contains(entryBlock))
        {
            return [];
        }

        var candidates = state.LoopsByHeader.Values
            .Where(loop => loop.Component.IsSubsetOf(component))
            .Where(loop => loop.Component.IsSubsetOf(allowed))
            .Where(loop => loop.Component.IsSubsetOf(dispatcherOwnedBlocks))
            .Where(loop => !loop.Component.Contains(entryBlock))
            .Where(loop => !loop.Component.Any(
                state.DispatchersByNode.ContainsKey))
            .ToArray();
        var nonOverlapping = candidates
            .Where(candidate => candidates.All(other =>
                candidate.Header == other.Header ||
                !candidate.Component.Overlaps(other.Component) ||
                candidate.Component.IsSubsetOf(other.Component) ||
                other.Component.IsSubsetOf(candidate.Component)))
            .ToArray();

        return nonOverlapping
            .Where(candidate => !nonOverlapping.Any(other =>
                candidate.Header != other.Header &&
                candidate.Component.IsProperSubsetOf(other.Component)))
            .SelectMany(loop => loop.Component)
            .ToImmutableHashSet();
    }
}
