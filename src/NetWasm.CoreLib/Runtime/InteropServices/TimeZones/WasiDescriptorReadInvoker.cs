namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class WasiDescriptorReadInvoker : IWasiDescriptorReadInvoker
    {
        public void Invoke(int handle, long length, long offset, nuint result) =>
            WasiTimeZoneImports.Read(handle, length, offset, result);
    }
}
