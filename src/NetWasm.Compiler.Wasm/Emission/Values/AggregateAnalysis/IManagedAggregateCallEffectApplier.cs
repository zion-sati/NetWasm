// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateCallEffectApplier
{
    // Applies a direct call to the addresses it consumes: an alias-preserving intrinsic retypes its address, a callee
    // with known by-ref effects materializes (and may reload) the storages it reads or writes, anything else rejects
    // them. Returns where the call's result points.
    ManagedAggregateAddressOrigins Apply(
        CilInstruction instruction,
        IReadOnlyList<ManagedAggregateAddressOrigins> stack,
        int consumed,
        ManagedAggregateAnalysisSession session);
}
