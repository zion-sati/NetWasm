namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IWasiEnvironmentInvoker
    {
        void Invoke(nuint result);
    }
}
