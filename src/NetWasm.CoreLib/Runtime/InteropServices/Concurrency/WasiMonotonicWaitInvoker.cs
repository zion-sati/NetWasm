namespace System.Runtime.InteropServices.Concurrency;

internal sealed class WasiMonotonicWaitInvoker : IWasiMonotonicWaitInvoker
{
    public int WaitFor(ulong nanoseconds) => WaitableSetImports.WaitFor(unchecked((long)nanoseconds));
}
