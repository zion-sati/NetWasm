namespace System.Runtime.InteropServices;

internal sealed class WasiBlockingOutputInvoker : IWasiBlockingOutputInvoker
{
    public void Invoke(int handle, nuint contents, nuint length, nuint result) =>
        WasiStreamImports.BlockingWriteAndFlush(handle, contents, length, result);
}
