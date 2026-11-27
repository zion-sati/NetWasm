// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IContinuationExtentFinder
{
    // Where the code a protected block's continuations start at ends: the boundary, or the method's end.
    int End(ControlFlowGraph graph, IReadOnlyCollection<int> targets, int boundary, int methodEnd);
}
