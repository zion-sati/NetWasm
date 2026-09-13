namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class WasiDescriptorOpenInvoker : IWasiDescriptorOpenInvoker
    {
        public void Invoke(
            int directoryHandle,
            nuint path,
            nuint pathLength,
            nuint result) => WasiTimeZoneImports.OpenAt(
                directoryHandle,
                0,
                path,
                pathLength,
                0,
                1,
                result);
    }
}
