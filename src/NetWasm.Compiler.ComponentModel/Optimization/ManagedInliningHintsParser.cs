// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel.Optimization;

public sealed class ManagedInliningHintsParser : IManagedInliningHintsParser
{
    private static readonly FrozenDictionary<string, WasmOptimizationOverrides?> Values =
        new Dictionary<string, WasmOptimizationOverrides?>(StringComparer.Ordinal)
        {
            ["Auto"] = null,
            ["IgnoreAggressive"] = new(ManagedInliningHints.IgnoreAggressive),
            ["Preserve"] = new(ManagedInliningHints.Preserve),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public WasmOptimizationOverrides? Parse(string? value) =>
        Values.TryGetValue(value ?? "Auto", out var options)
            ? options
            : throw ComponentException.Invalid(
                "Managed inlining hints must be Auto, IgnoreAggressive or Preserve.");
}
