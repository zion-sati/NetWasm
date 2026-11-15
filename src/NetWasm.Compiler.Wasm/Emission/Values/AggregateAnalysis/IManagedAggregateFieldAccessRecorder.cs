// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateFieldAccessRecorder
{
    // Records a field load or store (ldfld, stfld) through an address at the given origins as a lane access.
    void Record(
        CilInstruction instruction,
        StructuredBlockId block,
        ManagedAggregateAddressOrigins origins,
        bool writes,
        ManagedAggregateAnalysisSession session);
}
