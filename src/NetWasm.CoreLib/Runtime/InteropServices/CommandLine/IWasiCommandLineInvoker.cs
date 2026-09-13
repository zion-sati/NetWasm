namespace System.Runtime.InteropServices.CommandLine
{
    internal interface IWasiCommandLineInvoker
    {
        void Invoke(nuint result);
    }
}
