namespace System.Runtime.InteropServices.Output;

internal interface IWasiResultFutureDropInvoker
{
    void Drop(int readable);
}
