namespace System.Runtime.InteropServices.Concurrency;

internal sealed class WasiThreadYieldInvoker : IWasiThreadYieldInvoker
{
    // thread.yield reports whether the task was cancelled while it yielded; a write completes
    // either way, so the report is not needed here.
    public void Yield() => _ = WaitableSetImports.Yield();
}
