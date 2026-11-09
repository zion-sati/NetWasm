namespace System.Runtime.InteropServices.Output;

internal sealed class WasiOutputStreamOpener : IWasiOutputStreamOpener
{
    private readonly IWasiOutputStreamCreateInvoker _create;
    private readonly IWasiOutputStreamHandOffInvoker _handOff;

    internal WasiOutputStreamOpener(
        IWasiOutputStreamCreateInvoker create,
        IWasiOutputStreamHandOffInvoker handOff)
    {
        _create = create ?? throw new ArgumentNullException();
        _handOff = handOff ?? throw new ArgumentNullException();
    }

    public WasiOutputStreamEnds Open()
    {
        var ends = _create.Create();
        var readable = unchecked((int)ends);
        var writable = unchecked((int)(ends >> 32));
        return new WasiOutputStreamEnds(writable, _handOff.HandOff(readable));
    }
}
