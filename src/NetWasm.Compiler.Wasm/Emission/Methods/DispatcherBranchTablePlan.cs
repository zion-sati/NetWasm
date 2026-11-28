// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record DispatcherBranchTablePlan(
    ImmutableArray<DispatcherCasePlan> Cases,
    ImmutableArray<uint> Targets);

internal sealed record DispatcherCasePlan(
    int Target,
    StructuredDispatcherExit? Exit,
    StructuredDispatcherBlock? Block,
    DispatcherTransitionPlan? WhenTrue,
    DispatcherTransitionPlan? WhenFalse);

internal sealed record DispatcherTransitionPlan(
    StructuredBlockId Target,
    uint? DirectBranchDepth)
{
    // Whether the direct branch reaches one of the dispatcher's exits, which leaves the dispatcher.
    public bool LeavesDispatcher { get; init; }
}
