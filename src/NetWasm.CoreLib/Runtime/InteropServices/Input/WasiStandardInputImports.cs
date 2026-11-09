namespace System.Runtime.InteropServices.Input;

using System.Runtime.InteropServices.WebAssembly;

// wasi:cli/stdin: read-via-stream hands the guest the readable end of a stream<u8> carrying
// standard input and a future<result<_, error-code>> that reports whether the host read it
// without error. Index 0 is the stream and index 1 the future.
internal static class WasiStandardInputImports
{
    // Stores the readable end and the future at result.
    [WitImport("wasi:cli/stdin@0.3.2", "read-via-stream")]
    internal static extern void ReadViaStream(nuint result);

    [ComponentBuiltIn("wasi:cli/stdin@0.3.2", "[async-lower][stream-read-0]read-via-stream")]
    internal static extern int Read(int readable, nuint buffer, nuint length);

    [ComponentBuiltIn("wasi:cli/stdin@0.3.2", "[stream-drop-readable-0]read-via-stream")]
    internal static extern void DropReadable(int readable);

    [ComponentBuiltIn("wasi:cli/stdin@0.3.2", "[async-lower][future-read-1]read-via-stream")]
    internal static extern int ReadResult(int readable, nuint result);

    [ComponentBuiltIn("wasi:cli/stdin@0.3.2", "[future-drop-readable-1]read-via-stream")]
    internal static extern void DropResult(int readable);
}
