namespace System.Runtime.InteropServices.Cryptography
{
    using System.Runtime.InteropServices.WebAssembly;

    internal static class WasiRandomBytesImports
    {
        [WitImport("wasi:random/random@0.2.11", "get-random-bytes")]
        internal static extern void GetRandomBytes(long length, nuint result);
    }
}
