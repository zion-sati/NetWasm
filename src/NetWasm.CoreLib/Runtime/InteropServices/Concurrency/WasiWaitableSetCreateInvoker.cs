namespace System.Runtime.InteropServices.Concurrency;

internal sealed class WasiWaitableSetCreateInvoker : IWasiWaitableSetCreateInvoker
{
    public int Create() => WaitableSetImports.Create();
}
