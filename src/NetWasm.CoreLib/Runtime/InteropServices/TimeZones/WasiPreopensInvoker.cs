namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class WasiPreopensInvoker : IWasiPreopensInvoker
    {
        public void Invoke(nuint result) => WasiTimeZoneImports.GetDirectories(result);
    }
}
