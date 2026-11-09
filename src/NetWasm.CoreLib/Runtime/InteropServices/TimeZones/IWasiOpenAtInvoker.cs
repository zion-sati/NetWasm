namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IWasiOpenAtInvoker
    {
        // The async-lowered open-at: parameters and result in memory, the
        // subtask status returned.
        int Invoke(nuint parameters, nuint result);
    }
}
