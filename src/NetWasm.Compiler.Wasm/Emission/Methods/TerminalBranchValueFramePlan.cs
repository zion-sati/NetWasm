// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record TerminalBranchValueFramePlan(
    StructuredBlockOccurrence Condition,
    StructuredSequence WhenTrue,
    StructuredSequence WhenFalse,
    bool FramedWhenTrue,
    ImmutableHashSet<int> IncomingScalarLocals)
{
    // The framed arm also enters the root frame: no safepoint outside it holds a root.
    public bool DefersRootFrame { get; init; }
}
