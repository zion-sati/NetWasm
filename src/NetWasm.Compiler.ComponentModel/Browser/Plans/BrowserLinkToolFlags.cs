// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Frozen;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal static class BrowserLinkToolFlags
{
    internal static readonly FrozenSet<string> Features = new[]
    {
        "--enable-multimemory", "--enable-exception-handling", "--enable-reference-types", "--enable-multivalue",
        "--enable-bulk-memory", "--enable-nontrapping-float-to-int", "--enable-memory64",
        "--enable-simd",
    }.ToFrozenSet(StringComparer.Ordinal);

    internal static readonly FrozenSet<string> Optimization = new[]
    {
        "-O0", "-O1", "-O2", "-O3", "-Os", "-Oz",
    }.ToFrozenSet(StringComparer.Ordinal);

    internal static readonly FrozenSet<string> Passes = new[]
    {
        "--converge", "--low-memory-unused", "--remove-unused-module-elements", "--strip-debug",
    }.ToFrozenSet(StringComparer.Ordinal);

    // Options followed by a count, such as the size of a function Binaryen still inlines into its one caller.
    internal static readonly FrozenSet<string> CountedPasses = new[]
    {
        "--one-caller-inline-max-function-size",
    }.ToFrozenSet(StringComparer.Ordinal);
}
