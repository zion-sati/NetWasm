namespace System.Runtime.InteropServices.Output;

using System.Runtime.InteropServices.WebAssembly;

// wasi:cli/stderr: write-via-stream consumes the readable end of a stream<u8>
// and resolves its future<result<_, error-code>> when the host has written
// everything. Index 0 is the stream and index 1 the future.
internal static class WasiStandardErrorImports
{
    [ComponentBuiltIn("wasi:cli/stderr@0.3.2", "[stream-new-0]write-via-stream")]
    internal static extern long CreateStream();

    [WitImport("wasi:cli/stderr@0.3.2", "write-via-stream")]
    internal static extern int WriteViaStream(int readable);

    [ComponentBuiltIn("wasi:cli/stderr@0.3.2", "[async-lower][stream-write-0]write-via-stream")]
    internal static extern int Write(int writable, nuint contents, nuint length);

    [ComponentBuiltIn("wasi:cli/stderr@0.3.2", "[stream-drop-writable-0]write-via-stream")]
    internal static extern void DropWritable(int writable);

    [ComponentBuiltIn("wasi:cli/stderr@0.3.2", "[async-lower][future-read-1]write-via-stream")]
    internal static extern int ReadResult(int readable, nuint result);

    [ComponentBuiltIn("wasi:cli/stderr@0.3.2", "[future-drop-readable-1]write-via-stream")]
    internal static extern void DropResult(int readable);
}
