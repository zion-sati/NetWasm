// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateFieldAddressResolver
{
    // Where the address of a field (ldflda) of a value at the given origins points.
    ManagedAggregateAddressOrigins Resolve(
        CilInstruction instruction,
        ManagedAggregateAddressOrigins origins,
        ManagedAggregateAnalysisSession session);
}
