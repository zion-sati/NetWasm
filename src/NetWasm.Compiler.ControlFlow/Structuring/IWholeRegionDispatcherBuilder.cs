// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IWholeRegionDispatcherBuilder
{
    StructuredDispatcherDraft Build(ControlFlowStructuringState state, int? entry, ImmutableHashSet<int> component);
}
