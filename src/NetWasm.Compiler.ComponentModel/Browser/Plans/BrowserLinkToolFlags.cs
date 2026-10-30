using System;
using System.Collections.Frozen;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal static class BrowserLinkToolFlags
{
    internal static readonly FrozenSet<string> Features = new[]
    {
        "--enable-multimemory", "--enable-exception-handling", "--enable-reference-types",
        "--enable-bulk-memory", "--enable-nontrapping-float-to-int", "--enable-memory64",
        "--enable-simd",
    }.ToFrozenSet(StringComparer.Ordinal);

    internal static readonly FrozenSet<string> Optimization = new[]
    {
        "-O0", "-O1", "-O2", "-O3", "-Os", "-Oz",
    }.ToFrozenSet(StringComparer.Ordinal);

    internal static readonly FrozenSet<string> Passes = new[]
    {
        "--converge", "--remove-unused-module-elements", "--strip-debug",
    }.ToFrozenSet(StringComparer.Ordinal);
}
