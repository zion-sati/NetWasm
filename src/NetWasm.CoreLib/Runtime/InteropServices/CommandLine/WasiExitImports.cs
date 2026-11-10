namespace System.Runtime.InteropServices.CommandLine
{
    using System.Runtime.InteropServices.WebAssembly;

    // wasi:cli/exit. exit takes result<_, _> as its discriminant, zero for ok; exit-with-code takes
    // the status code's eight bits. Neither returns.
    internal static class WasiExitImports
    {
        [WitImport("wasi:cli/exit@0.3.2", "exit")]
        internal static extern void Exit(int status);

        [WitImport("wasi:cli/exit@0.3.2", "exit-with-code")]
        internal static extern void ExitWithCode(int statusCode);
    }
}
