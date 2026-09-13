namespace System.Runtime.InteropServices.CommandLine
{
    using System.Runtime.InteropServices.WebAssembly;

    internal static class WasiCommandLineImports
    {
        [WitImport("wasi:cli@0.2.11/environment", "get-arguments")]
        internal static extern void GetArguments(nuint result);

        [WitImport("wasi:cli@0.2.11/environment", "initial-cwd")]
        internal static extern void GetInitialWorkingDirectory(nuint result);
    }
}
