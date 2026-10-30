namespace System.Runtime.InteropServices;

using WebAssembly;

internal static class WasiStreamImports
{
    [WitImport("wasi:cli/stdout@0.2.11", "get-stdout")]
    internal static extern int GetStandardOutput();

    [WitImport("wasi:cli/stderr@0.2.11", "get-stderr")]
    internal static extern int GetStandardError();

    [WitImport("wasi:io/streams@0.2.11", "[method]output-stream.check-write")]
    internal static extern void CheckWrite(int handle, nuint result);

    [WitImport("wasi:io/streams@0.2.11", "[method]output-stream.write")]
    internal static extern void Write(
        int handle,
        nuint contents,
        nuint length,
        nuint result);

    [WitImport("wasi:io/streams@0.2.11", "[method]output-stream.blocking-write-and-flush")]
    internal static extern void BlockingWriteAndFlush(
        int handle,
        nuint contents,
        nuint length,
        nuint result);

    [WitImport("wasi:io/streams@0.2.11", "[resource-drop]output-stream")]
    internal static extern void DropOutputStream(int handle);

    [WitImport("wasi:io/error@0.2.11", "[resource-drop]error")]
    internal static extern void DropError(int handle);
}
