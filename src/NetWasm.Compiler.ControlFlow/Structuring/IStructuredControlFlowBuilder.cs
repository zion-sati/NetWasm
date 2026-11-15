// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IStructuredControlFlowBuilder
{
    StructuredSequenceDraft Build(ControlFlowStructuringState state, int? start, int? stop, ImmutableHashSet<int> allowed, HashSet<int> path);

    StructuredDispatcherDraft Build(
        ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? exitStop,
        HashSet<int> path);

}
