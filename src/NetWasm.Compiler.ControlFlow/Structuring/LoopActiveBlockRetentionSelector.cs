// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class LoopActiveBlockRetentionSelector : ILoopActiveBlockRetentionSelector
{
    public ImmutableHashSet<int> Select(
        ControlFlowStructuringState state,
        LoopRegion loop)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(loop);

        return loop.CoreComponent
            .Where(block => !state.DispatchersByNode.ContainsKey(block))
            .ToImmutableHashSet();
    }
}
