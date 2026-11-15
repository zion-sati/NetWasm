// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

internal static class RuntimeGarbageCollectors
{
    public const string Compact = "Compact";
    public const string NetWasm = "NetWasm";
    public const string Bump = "Bump";
    public const string ValidationMessage =
        "The NetWasm garbage collector must be Compact, NetWasm or Bump.";
    public const int TargetCount = 6;

    public static bool IsSupported(string? value) =>
        value is Compact or NetWasm or Bump;
}
