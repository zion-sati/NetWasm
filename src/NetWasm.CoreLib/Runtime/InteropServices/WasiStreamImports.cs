namespace System.Runtime.InteropServices;

using WebAssembly;

internal static class WasiStreamImports
{
    [WitImport("wasi:cli@0.2.11/stdout", "get-stdout")]
    internal static extern int GetStandardOutput();

    [WitImport("wasi:cli@0.2.11/stderr", "get-stderr")]
    internal static extern int GetStandardError();

    [WitImport("wasi:io@0.2.11/streams", "[method]output-stream.check-write")]
    internal static extern void CheckWrite(int handle, nuint result);

    [WitImport("wasi:io@0.2.11/streams", "[method]output-stream.write")]
    internal static extern void Write(
        int handle,
        nuint contents,
        nuint length,
        nuint result);

    [WitImport("wasi:io@0.2.11/streams", "[method]output-stream.blocking-write-and-flush")]
    internal static extern void BlockingWriteAndFlush(
        int handle,
        nuint contents,
        nuint length,
        nuint result);

    [WitImport("wasi:io@0.2.11/streams", "[resource-drop]output-stream")]
    internal static extern void DropOutputStream(int handle);

    [WitImport("wasi:io@0.2.11/error", "[resource-drop]error")]
    internal static extern void DropError(int handle);
}
