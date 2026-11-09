namespace System.Runtime.InteropServices.ConsoleServices;

using System.Runtime.InteropServices.WebAssembly;

// The wasi:cli terminal bindings behind the terminal detector's invoker contracts.
internal sealed class WasiTerminalInvoker : IWasiTerminalInvoker
{
    public int GetTerminal(StandardStream stream)
    {
        var result = CanonicalAbi.Allocate(8, 4);
        try
        {
            switch (stream)
            {
                case StandardStream.Input:
                    WasiTerminalImports.GetTerminalStdin(result);
                    break;
                case StandardStream.Output:
                    WasiTerminalImports.GetTerminalStdout(result);
                    break;
                default:
                    WasiTerminalImports.GetTerminalStderr(result);
                    break;
            }

            return CanonicalAbi.ReadByte(result, 0) == 0 ? 0 : CanonicalAbi.ReadInt32(result, 4);
        }
        finally
        {
            CanonicalAbi.Free(result);
        }
    }
}

internal sealed class WasiTerminalDropInvoker : IWasiTerminalDropInvoker
{
    public void Drop(StandardStream stream, int handle)
    {
        if (stream == StandardStream.Input)
        {
            WasiTerminalImports.DropTerminalInput(handle);
        }
        else
        {
            WasiTerminalImports.DropTerminalOutput(handle);
        }
    }
}
