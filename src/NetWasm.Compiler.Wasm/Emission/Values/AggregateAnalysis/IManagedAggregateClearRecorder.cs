// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateClearRecorder
{
    // Records an initobj through an address at the given origins as a zeroing write of the lanes it covers.
    void Record(
        CilInstruction instruction,
        StructuredBlockId block,
        ManagedAggregateAddressOrigins origins,
        ManagedAggregateAnalysisSession session);
}
