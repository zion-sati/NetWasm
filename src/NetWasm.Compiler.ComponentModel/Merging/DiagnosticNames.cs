// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Merging;

// A merge keeps its functions' names for debug information (NetWasmDebugInfo), and for a diagnostic twin, built with
// NETWASM_KEEP_NAMES set and never timed, so profiles and listings can name them; a product module keeps none.
internal static class DiagnosticNames
{
    public static bool KeptWith(bool debugInformation) =>
        KeptFor(Environment.GetEnvironmentVariable("NETWASM_KEEP_NAMES"), debugInformation);

    // Whether names are kept: with debug information, or for any NETWASM_KEEP_NAMES value but an empty one.
    internal static bool KeptFor(string? value, bool debugInformation = false) =>
        debugInformation || value is { Length: > 0 };
}
