// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

internal sealed class WasiOutputStreamOpener<TStream> : IWasiOutputStreamOpener<TStream>
{
    private readonly IWasiOutputStreamCreateInvoker<TStream> _create;
    private readonly IWasiOutputStreamHandOffInvoker<TStream> _handOff;

    internal WasiOutputStreamOpener(
        IWasiOutputStreamCreateInvoker<TStream> create,
        IWasiOutputStreamHandOffInvoker<TStream> handOff)
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
