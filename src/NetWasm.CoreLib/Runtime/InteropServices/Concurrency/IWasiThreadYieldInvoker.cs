namespace System.Runtime.InteropServices.Concurrency;

internal interface IWasiThreadYieldInvoker
{
    // Lets the host and other tasks run before the current task continues; where the task cannot
    // block, returns at once.
    void Yield();
}
