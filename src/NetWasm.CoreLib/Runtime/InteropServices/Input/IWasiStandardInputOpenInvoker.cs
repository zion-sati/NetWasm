namespace System.Runtime.InteropServices.Input;

using System.Runtime.InteropServices.Streams;

internal interface IWasiStandardInputOpenInvoker
{
    // Calls read-via-stream and returns the guest's ends.
    WasiReadableStreamEnds Open();
}
