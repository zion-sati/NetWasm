// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ILoopContinueTargetFinder
{
    int Find(ControlFlowStructuringState state, ControlFlowGraph graph, ImmutableHashSet<int> component, int header);
}
