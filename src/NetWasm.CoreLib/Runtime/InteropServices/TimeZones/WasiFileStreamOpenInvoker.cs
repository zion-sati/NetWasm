namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.Streams;
    using System.Runtime.InteropServices.WebAssembly;

    internal sealed class WasiFileStreamOpenInvoker : IWasiFileStreamOpenInvoker
    {
        public WasiReadableStreamEnds Open(int handle, long offset)
        {
            var result = CanonicalAbi.Allocate(8, 4);
            try
            {
                WasiTimeZoneImports.ReadViaStream(handle, offset, result);
                return new WasiReadableStreamEnds(
                    CanonicalAbi.ReadInt32(result, 0),
                    CanonicalAbi.ReadInt32(result, 4));
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }
    }
}
