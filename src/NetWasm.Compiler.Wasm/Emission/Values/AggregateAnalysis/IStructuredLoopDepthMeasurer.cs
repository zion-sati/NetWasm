// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IStructuredLoopDepthMeasurer
{
    // How many loops enclose each block; a replicated block takes its deepest occurrence.
    Dictionary<StructuredBlockId, int> Measure(StructuredSequence body);
}
