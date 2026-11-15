// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using StructuredMethod = global::NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;

namespace NetWasm.Compiler.Diagnostics;

internal interface IStructuredBlockOwnershipAnalyzer
{
    ImmutableArray<StructuredBlockOwnershipVisit> Analyze(StructuredMethod method);
}
