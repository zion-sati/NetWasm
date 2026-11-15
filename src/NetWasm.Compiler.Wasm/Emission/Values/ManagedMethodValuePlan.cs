// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed record ManagedMethodValuePlan(
    MethodRootMap EmissionRootMap,
    ManagedAggregateSsaPlan Aggregates)
{
    public static ManagedMethodValuePlan Unchanged(MethodRootMap rootMap) =>
        new(rootMap, ManagedAggregateSsaPlan.Empty);
}

internal sealed record ManagedMethodValuePlanRequest(
    StructuredMethod Method,
    MethodRootMap RootMap,
    IEntryLookup<string, ManagedByRefMethodEffects> ByRefEffects,
    ImmutableHashSet<ManagedAggregateStorageId> ReservedStorages,
    bool Enabled)
{
    // Whether aggregates with reference and byref fields take part.
    public bool References { get; init; }
}
