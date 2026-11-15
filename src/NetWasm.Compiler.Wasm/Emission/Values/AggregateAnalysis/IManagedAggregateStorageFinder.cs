// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateStorageFinder
{
    // The address-taken arguments and locals whose values could live in lanes.
    Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> Find(ManagedAggregateValueAnalysisRequest request);
}
