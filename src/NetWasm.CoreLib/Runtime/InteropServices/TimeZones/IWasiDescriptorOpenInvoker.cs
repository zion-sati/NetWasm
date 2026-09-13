namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IWasiDescriptorOpenInvoker
    {
        void Invoke(
            int directoryHandle,
            nuint path,
            nuint pathLength,
            nuint result);
    }
}
