namespace System.Runtime.InteropServices.Concurrency;

internal sealed unsafe class WasiWaitableSetWaitInvoker : IWasiWaitableSetWaitInvoker
{
    public WaitableEvent Wait(int set)
    {
        // waitable-set.wait stores the waitable and the payload as two u32s.
        var payload = stackalloc uint[2];
        var code = WaitableSetImports.Wait(set, (nuint)payload);
        return new WaitableEvent(code, unchecked((int)payload[0]), payload[1]);
    }
}
