// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed record AddressTakenScalarPromotionPlan(
    ImmutableDictionary<int, CliTypeIdentity> PromotedLocals,
    ImmutableDictionary<int, AddressTakenScalarCallSynchronization> CallSynchronizations)
{
    public static AddressTakenScalarPromotionPlan Empty { get; } = new([], []);
}

internal sealed record AddressTakenScalarCallSynchronization(
    ImmutableArray<int> LocalIndices,
    ImmutableArray<int> ReloadLocalIndices);
