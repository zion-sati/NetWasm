namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.WebAssembly;

    internal static class WasiTimeZoneImports
    {
        [WitImport("wasi:cli/environment@0.2.11", "get-environment")]
        internal static extern void GetEnvironment(nuint result);

        [WitImport("wasi:filesystem/preopens@0.2.11", "get-directories")]
        internal static extern void GetDirectories(nuint result);

        [WitImport("wasi:filesystem/types@0.2.11", "[method]descriptor.open-at")]
        internal static extern void OpenAt(
            int directoryHandle,
            int pathFlags,
            nuint path,
            nuint pathLength,
            int openFlags,
            int descriptorFlags,
            nuint result);

        [WitImport("wasi:filesystem/types@0.2.11", "[method]descriptor.read")]
        internal static extern void Read(
            int handle,
            long length,
            long offset,
            nuint result);
    }
}
