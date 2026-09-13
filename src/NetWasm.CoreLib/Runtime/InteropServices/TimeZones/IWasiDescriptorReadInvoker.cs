namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IWasiDescriptorReadInvoker
    {
        void Invoke(int handle, long length, long offset, nuint result);
    }
}
