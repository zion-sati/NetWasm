namespace System.Runtime.InteropServices.CommandLine
{
    using System.Runtime.InteropServices.WebAssembly;

    internal static class WasiCommandLineImports
    {
        [WitImport("wasi:cli/environment@0.3.2", "get-arguments")]
        internal static extern void GetArguments(nuint result);

        [WitImport("wasi:cli/environment@0.3.2", "get-initial-cwd")]
        internal static extern void GetInitialWorkingDirectory(nuint result);
    }
}
