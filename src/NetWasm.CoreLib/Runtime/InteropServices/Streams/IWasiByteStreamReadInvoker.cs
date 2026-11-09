namespace System.Runtime.InteropServices.Streams;

internal interface IWasiByteStreamReadInvoker
{
    // An async-lowered stream.read: BLOCKED (-1), or the copied count shifted left by 4 with
    // the copy result in the low 4 bits.
    int Read(int readable, nuint buffer, nuint length);
}
