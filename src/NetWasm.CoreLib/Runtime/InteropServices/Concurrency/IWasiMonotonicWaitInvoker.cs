namespace System.Runtime.InteropServices.Concurrency;

internal interface IWasiMonotonicWaitInvoker
{
    int WaitFor(ulong nanoseconds);
}
