// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeOptimizationPolicyResolver
{
    RuntimeOptimizationPolicy Resolve(RuntimeWasmOptimization optimization);
}
