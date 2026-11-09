namespace System.Runtime.InteropServices.ConsoleServices;

using System.Runtime.InteropServices.WebAssembly;

internal interface IWasiTerminalDropInvoker
{
    // Drops the terminal resource the stream's get-terminal function returned.
    void Drop(StandardStream stream, int handle);
}
