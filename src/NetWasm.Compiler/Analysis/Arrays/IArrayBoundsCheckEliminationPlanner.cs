// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Arrays;

// The offsets of the element accesses whose index is in bounds, and of the
// branches of an index check against a length bound that always go one way.
internal interface IArrayBoundsCheckEliminationPlanner
{
    ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow);
}
