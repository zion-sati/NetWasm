// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IStackTraceMethodPlanBuilder
{
    StackTraceMethodPlan Build(
        bool enabled,
        ImmutableArray<EntityKey> directMethods,
        ImmutableArray<StackTraceConstructedMethod> constructedMethods,
        ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>
            sourceLocations);
}
