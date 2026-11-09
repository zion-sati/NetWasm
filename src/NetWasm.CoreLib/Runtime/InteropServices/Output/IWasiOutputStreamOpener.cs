namespace System.Runtime.InteropServices.Output;

internal interface IWasiOutputStreamOpener
{
    WasiOutputStreamEnds Open();
}
