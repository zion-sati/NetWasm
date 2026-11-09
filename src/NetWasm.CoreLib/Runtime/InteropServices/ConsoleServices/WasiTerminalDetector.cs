namespace System.Runtime.InteropServices.ConsoleServices;

using System.Runtime.InteropServices.WebAssembly;

// A standard stream is a terminal when the host hands over a terminal resource for it, which
// the detector drops at once: NetWasm uses no terminal operations.
internal sealed class WasiTerminalDetector(IWasiTerminalInvoker terminals, IWasiTerminalDropInvoker drop) :
    ITerminalDetector
{
    private readonly IWasiTerminalInvoker _terminals = terminals ?? throw new ArgumentNullException(nameof(terminals));
    private readonly IWasiTerminalDropInvoker _drop = drop ?? throw new ArgumentNullException(nameof(drop));

    public bool IsTerminal(StandardStream stream)
    {
        if (stream is not (StandardStream.Input or StandardStream.Output or StandardStream.Error))
        {
            throw new ArgumentOutOfRangeException(nameof(stream));
        }

        var handle = _terminals.GetTerminal(stream);
        if (handle == 0)
        {
            return false;
        }

        _drop.Drop(stream, handle);
        return true;
    }
}
