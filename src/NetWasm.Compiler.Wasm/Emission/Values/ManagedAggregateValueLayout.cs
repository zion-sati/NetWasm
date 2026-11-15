// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed record ManagedAggregateValueLayout(
    ManagedAggregateSsaPlan Plan,
    ImmutableDictionary<ManagedAggregateSsaLane, int> LaneLocals)
{
    public static ManagedAggregateValueLayout Empty { get; } =
        new(ManagedAggregateSsaPlan.Empty, []);
}
