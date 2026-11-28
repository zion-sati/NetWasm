// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ITerminatingExitJoinFinder
{
    // Where a loop's exits meet once the paths that end the region are set aside, or null.
    int? Find(ControlFlowGraph graph, IReadOnlyCollection<int> exits, ImmutableHashSet<int> loop,
        ImmutableHashSet<int> allowed);
}
