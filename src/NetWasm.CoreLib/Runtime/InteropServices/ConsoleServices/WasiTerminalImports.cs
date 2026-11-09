namespace System.Runtime.InteropServices.ConsoleServices;

using System.Runtime.InteropServices.WebAssembly;

// wasi:cli's terminal interfaces: each get-terminal function stores option<terminal-input> or
// option<terminal-output> at result (a discriminant byte, then the handle at offset 4), some
// when the host's standard stream is a terminal.
internal static class WasiTerminalImports
{
    [WitImport("wasi:cli/terminal-stdin@0.3.2", "get-terminal-stdin")]
    internal static extern void GetTerminalStdin(nuint result);

    [WitImport("wasi:cli/terminal-stdout@0.3.2", "get-terminal-stdout")]
    internal static extern void GetTerminalStdout(nuint result);

    [WitImport("wasi:cli/terminal-stderr@0.3.2", "get-terminal-stderr")]
    internal static extern void GetTerminalStderr(nuint result);

    [WitImport("wasi:cli/terminal-input@0.3.2", "[resource-drop]terminal-input")]
    internal static extern void DropTerminalInput(int handle);

    [WitImport("wasi:cli/terminal-output@0.3.2", "[resource-drop]terminal-output")]
    internal static extern void DropTerminalOutput(int handle);
}
