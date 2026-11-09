namespace System.Runtime.InteropServices.Concurrency;

internal interface IWasiWaitableSetWaitInvoker
{
    WaitableEvent Wait(int set);
}
