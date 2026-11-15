// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeOptimizationPolicyResolver : IRuntimeOptimizationPolicyResolver
{
    private static readonly FrozenDictionary<RuntimeWasmOptimization, RuntimeOptimizationPolicy> Policies =
        new Dictionary<RuntimeWasmOptimization, RuntimeOptimizationPolicy>
        {
            [RuntimeWasmOptimization.None] = new(null, false),
            [RuntimeWasmOptimization.O0] = new("-O0", false),
            [RuntimeWasmOptimization.O1] = new("-O1", true),
            [RuntimeWasmOptimization.O2] = new("-O2", true),
            [RuntimeWasmOptimization.O3] = new("-O3", true),
            [RuntimeWasmOptimization.Os] = new("-Os", false),
            [RuntimeWasmOptimization.Oz] = new("-Oz", false),
        }.ToFrozenDictionary();

    public RuntimeOptimizationPolicy Resolve(RuntimeWasmOptimization optimization) =>
        Policies.TryGetValue(optimization, out var policy)
            ? policy
            : throw new ArgumentOutOfRangeException(nameof(optimization));
}
