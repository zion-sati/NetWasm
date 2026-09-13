namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IWasiPreopensInvoker
    {
        void Invoke(nuint result);
    }
}
