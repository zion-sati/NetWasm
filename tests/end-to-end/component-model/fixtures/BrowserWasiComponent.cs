using System;
using System.Runtime.InteropServices.WebAssembly;

namespace NetWasm.Fixtures.ComponentModel;

public static class BrowserWasiComponent
{
    [WitExport("netwasm:test-wasi@1.0.0/acceptance", "sample")]
    public static ulong Sample() => GetRandomU64();

    public static int Run(int value) => value;

    [WitImport("wasi:random@0.2.11/random", "get-random-u64")]
    private static extern long GetRandomU64Canonical();

    [WitImport("wasi:random@0.2.11/random", "get-random-bytes")]
    private static extern void GetRandomBytesCanonical(long length, nuint result);

    private static ulong GetRandomU64() => unchecked((ulong)GetRandomU64Canonical());
}
