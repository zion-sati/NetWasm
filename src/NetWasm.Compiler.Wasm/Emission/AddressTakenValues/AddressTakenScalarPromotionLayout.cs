// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed record AddressTakenScalarPromotionLayout(
    AddressTakenScalarPromotionPlan Plan,
    ImmutableDictionary<int, int> ShadowLocals)
{
    public static AddressTakenScalarPromotionLayout Empty { get; } = new(
        AddressTakenScalarPromotionPlan.Empty,
        []);
}
