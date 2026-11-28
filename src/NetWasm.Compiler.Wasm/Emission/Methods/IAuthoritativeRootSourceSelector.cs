// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IAuthoritativeRootSourceSelector
{
    // The context is the method's before selection: its root map, value layout and filter environment, with no
    // authoritative slots.
    ImmutableHashSet<RootSource> Select(StructuredMethod method, MethodEmissionContext context, ArrayMetadataCachingPlan arrays);
}
