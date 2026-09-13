namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.WebAssembly;

    internal static class WasiTimeZoneImports
    {
        [WitImport("wasi:cli@0.2.11/environment", "get-environment")]
        internal static extern void GetEnvironment(nuint result);

        [WitImport("wasi:filesystem@0.2.11/preopens", "get-directories")]
        internal static extern void GetDirectories(nuint result);

        [WitImport("wasi:filesystem@0.2.11/types", "[method]descriptor.open-at")]
        internal static extern void OpenAt(
            int directoryHandle,
            int pathFlags,
            nuint path,
            nuint pathLength,
            int openFlags,
            int descriptorFlags,
            nuint result);

        [WitImport("wasi:filesystem@0.2.11/types", "[method]descriptor.read")]
        internal static extern void Read(
            int handle,
            long length,
            long offset,
            nuint result);

        [WitImport("wasi:filesystem@0.2.11/types", "[resource-drop]descriptor")]
        internal static extern void DropDescriptor(int handle);
    }
}
