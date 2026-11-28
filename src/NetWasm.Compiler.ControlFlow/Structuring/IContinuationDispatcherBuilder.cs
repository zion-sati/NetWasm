// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Generic;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IContinuationDispatcherBuilder
{
    // A dispatcher over the blocks the targets reach before end.
    StructuredDispatcherDraft Build(ControlFlowStructuringState state, IEnumerable<int> targets, int end);
}
