namespace System.Runtime.InteropServices.Concurrency;

internal sealed class WasiWaitableJoinInvoker : IWasiWaitableJoinInvoker
{
    public void Join(int waitable, int set) => WaitableSetImports.Join(waitable, set);
}
