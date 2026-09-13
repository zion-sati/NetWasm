namespace System.Runtime.InteropServices;

internal interface IWasiBlockingOutputInvoker
{
    void Invoke(int handle, nuint contents, nuint length, nuint result);
}
