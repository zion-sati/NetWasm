// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class DispatcherExitSelector : IDispatcherExitSelector
{
    public int[] Select(IEnumerable<int> successors, IEnumerable<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(successors);
        ArgumentNullException.ThrowIfNull(allowed);

        return successors.Where(allowed.Contains).ToArray();
    }
}
