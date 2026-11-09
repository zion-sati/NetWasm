namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.WebAssembly;

    internal static class WasiTimeZoneImports
    {
        [WitImport("wasi:cli/environment@0.3.2", "get-environment")]
        internal static extern void GetEnvironment(nuint result);

        [WitImport("wasi:filesystem/preopens@0.3.2", "get-directories")]
        internal static extern void GetDirectories(nuint result);

        // open-at is an async function; its six flat parameters pass through
        // memory, and the call returns its subtask's status.
        [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.open-at")]
        internal static extern int OpenAt(nuint parameters, nuint result);

        // Stores the readable end of a stream<u8> and its completion
        // future<result<_, error-code>> (indices 0 and 1) at result.
        [WitImport("wasi:filesystem/types@0.3.2", "[method]descriptor.read-via-stream")]
        internal static extern void ReadViaStream(int handle, long offset, nuint result);

        [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][stream-read-0][method]descriptor.read-via-stream")]
        internal static extern int Read(int readable, nuint buffer, nuint length);

        [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[stream-drop-readable-0][method]descriptor.read-via-stream")]
        internal static extern void DropReadable(int readable);

        [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][future-read-1][method]descriptor.read-via-stream")]
        internal static extern int ReadResult(int readable, nuint result);

        [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[future-drop-readable-1][method]descriptor.read-via-stream")]
        internal static extern void DropResult(int readable);
    }
}
