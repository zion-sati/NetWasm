namespace System.Runtime.InteropServices.Output;

internal interface IWasiOutputStreamHandOffInvoker
{
    // Gives the host the readable end and returns the completion future.
    int HandOff(int readable);
}
