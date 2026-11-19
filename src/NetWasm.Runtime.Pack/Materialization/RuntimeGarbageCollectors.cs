// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal static class RuntimeGarbageCollectors
{
    public const string Compact = "Compact";
    public const string NetWasm = "NetWasm";
    public const string Bump = "Bump";
    // NetWasmGC with generations: a program built for it marks a card at each store of a reference.
    public const string NetWasmGen = "NetWasmGen";
    public const string ValidationMessage =
        "The NetWasm garbage collector must be Compact, NetWasm, NetWasmGen or Bump.";
    public const int TargetCount = 8;

    public const string UnrecordedWritesMessage =
        "The NetWasmGen garbage collector needs a program compiled for it, which records its stores of references; " +
        "this program was compiled for another collector. Build it with NetWasmGarbageCollector set to NetWasmGen.";
    private const string ReferenceWriteBarrierFeature = "reference-write-barrier";

    public static bool IsSupported(string? value) =>
        value is Compact or NetWasm or NetWasmGen or Bump;

    // NetWasmGen finds an older object's newer ones only through the stores the program records, so with a
    // program compiled without them it would free objects still in use. A program that says nothing of its
    // features comes from a compiler that knew no such collector.
    public static void RequireProgramFor(string garbageCollector, ImmutableArray<string> runtimeFeatures)
    {
        if (garbageCollector == NetWasmGen &&
            (runtimeFeatures.IsDefault || !runtimeFeatures.Contains(ReferenceWriteBarrierFeature)))
            throw new InvalidOperationException(UnrecordedWritesMessage);
    }
}
