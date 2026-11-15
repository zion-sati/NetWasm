// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

// The address origins of each stack slot and local where a block starts or leaves.
internal sealed record ManagedAggregateFlowState(
    ImmutableArray<ManagedAggregateAddressOrigins> Stack,
    ImmutableArray<ManagedAggregateAddressOrigins> Locals);
