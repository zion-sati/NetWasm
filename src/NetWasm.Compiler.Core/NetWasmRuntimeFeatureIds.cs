// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

public static class NetWasmRuntimeFeatureIds
{
    public const string ArraySetValue = "array-set-value";
    public const string EphemeronHandles = "ephemeron-handles";
    public const string Finalization = "finalization";
    public const string LocalTime = "local-time";
    public const string ObjectClone = "object-clone";
    public const string RandomSeed = "random-seed";
    public const string StructuredCommandDiagnostics = "structured-command-diagnostics";
    public const string WeakHandles = "weak-handles";

    // Every feature a compiler may record, in ordinal order. The runtime pack's layout
    // reader and the hosting runtime-feature-ids.mjs mirror it; their tests fail on drift.
    public static ImmutableArray<string> All { get; } =
    [
        ArraySetValue,
        EphemeronHandles,
        Finalization,
        LocalTime,
        ObjectClone,
        RandomSeed,
        StructuredCommandDiagnostics,
        WeakHandles,
    ];
}
