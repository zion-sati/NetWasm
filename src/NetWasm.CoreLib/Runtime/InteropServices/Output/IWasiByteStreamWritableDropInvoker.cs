namespace System.Runtime.InteropServices.Output;

internal interface IWasiByteStreamWritableDropInvoker
{
    void Drop(int writable);
}
