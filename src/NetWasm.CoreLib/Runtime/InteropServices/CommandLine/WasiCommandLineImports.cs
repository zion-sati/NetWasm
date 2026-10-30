namespace System.Runtime.InteropServices.CommandLine
{
    using System.Runtime.InteropServices.WebAssembly;

    internal static class WasiCommandLineImports
    {
        [WitImport("wasi:cli/environment@0.2.11", "get-arguments")]
        internal static extern void GetArguments(nuint result);

        [WitImport("wasi:cli/environment@0.2.11", "initial-cwd")]
        internal static extern void GetInitialWorkingDirectory(nuint result);
    }
}
