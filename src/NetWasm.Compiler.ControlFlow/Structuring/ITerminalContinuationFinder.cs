// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ITerminalContinuationFinder
{
    // The blocks of a protected block's continuation that cannot complete normally, or null.
    ImmutableHashSet<int>? Find(ControlFlowGraph graph, int target, int end, IReadOnlyCollection<int> otherTargets);
}
