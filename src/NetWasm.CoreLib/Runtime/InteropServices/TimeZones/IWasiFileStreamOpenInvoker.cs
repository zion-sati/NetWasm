namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.Streams;

    internal interface IWasiFileStreamOpenInvoker
    {
        WasiReadableStreamEnds Open(int handle, long offset);
    }
}
