namespace System.Runtime.InteropServices.Output;

internal interface IWasiOutputStreamCloser
{
    void Close(WasiOutputStreamEnds ends);
}
