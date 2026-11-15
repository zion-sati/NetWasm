// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IUnamortizedAggregateStoreRejector
{
    // Rejects the storages whose whole-value stores are not outside every loop that accesses their lanes.
    void Reject(ManagedAggregateAnalysisSession session);
}
