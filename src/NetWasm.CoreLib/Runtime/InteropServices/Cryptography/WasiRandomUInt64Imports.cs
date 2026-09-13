namespace System.Runtime.InteropServices.Cryptography
{
    using System.Runtime.InteropServices.WebAssembly;

    internal static class WasiRandomUInt64Imports
    {
        // The standard interface declares both random functions, so selecting its
        // world requires a managed binding even though CoreLib consumes bytes only.
        [WitImport("wasi:random@0.2.11/random", "get-random-u64")]
        internal static extern long GetRandomUInt64();
    }
}
