namespace System.Runtime.InteropServices.Output;

internal interface IWasiOutputStreamCreateInvoker
{
    // stream.new: the readable end in the low 32 bits, the writable end above.
    long Create();
}
