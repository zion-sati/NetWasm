// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IAuthoritativeRootSourceSelector
{
    ImmutableHashSet<RootSource> Select(MethodRootMap roots, ValueFrameLayout values,
        FilterEnvironmentLayout filters, ArrayMetadataCachingPlan arrays);
}
