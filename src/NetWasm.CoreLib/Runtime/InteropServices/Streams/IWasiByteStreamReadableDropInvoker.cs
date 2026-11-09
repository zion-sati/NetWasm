namespace System.Runtime.InteropServices.Streams;

internal interface IWasiByteStreamReadableDropInvoker
{
    void Drop(int readable);
}
