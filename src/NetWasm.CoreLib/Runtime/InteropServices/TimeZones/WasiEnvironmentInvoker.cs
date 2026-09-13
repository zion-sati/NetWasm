namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class WasiEnvironmentInvoker : IWasiEnvironmentInvoker
    {
        public void Invoke(nuint result) =>
            WasiTimeZoneImports.GetEnvironment(result);
    }
}
