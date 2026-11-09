namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class WasiOpenAtInvoker : IWasiOpenAtInvoker
    {
        public int Invoke(nuint parameters, nuint result) =>
            WasiTimeZoneImports.OpenAt(parameters, result);
    }
}
