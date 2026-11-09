namespace System.Runtime.InteropServices.Output;

internal interface IWasiByteStreamWriteInvoker
{
    // An async-lowered stream.write: BLOCKED (-1), or the copied count shifted
    // left by 4 with the copy result in the low 4 bits.
    int Write(int writable, nuint contents, nuint length);
}
