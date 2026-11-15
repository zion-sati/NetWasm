// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.CallLifetimes;

internal sealed record ScalarCallLifetimePlan(
    ImmutableDictionary<int, ImmutableArray<int>> CallLocals)
{
    public static ScalarCallLifetimePlan Empty { get; } = new(
        ImmutableDictionary<int, ImmutableArray<int>>.Empty);
}
