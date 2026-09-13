namespace System.Runtime.InteropServices.CommandLine
{
    internal sealed class WasiCommandLineInvoker : IWasiCommandLineInvoker
    {
        public void Invoke(nuint result) =>
            WasiCommandLineImports.GetArguments(result);
    }
}
