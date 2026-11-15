// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ActiveControlFlowBlockClipper : IActiveControlFlowBlockClipper
{
    public ImmutableHashSet<int> Clip(
        ImmutableHashSet<int> allowed,
        IReadOnlySet<int> activePath,
        IReadOnlySet<int> retained)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(activePath);
        ArgumentNullException.ThrowIfNull(retained);

        var clipped = allowed;
        foreach (var block in activePath)
        {
            if (!retained.Contains(block))
            {
                clipped = clipped.Remove(block);
            }
        }

        return clipped;
    }
}
