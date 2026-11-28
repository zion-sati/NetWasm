// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

// One standard stream's write-via-stream, opened on first write and closed once.
// The opened streams form a list that EndOpened closes when the process exits,
// as an operating system closes a process's descriptors and flushes their output.
// TStream names the stream, so each stream keeps its own list. Close is not
// IDisposable.Dispose: at O0 an interface call dispatches to every allocated
// implementation, so the program's own Dispose calls must not reach a stream's
// imports.
internal sealed class WasiOutputStreamResource<TStream>
{
    private static WasiOutputStreamResource<TStream>? s_opened;

    private readonly IWasiOutputStreamOpener<TStream> _opener;
    private readonly IWasiOutputStreamCloser<TStream> _closer;
    private WasiOutputStreamEnds _ends;
    private bool _opened;
    private bool _closed;
    private WasiOutputStreamResource<TStream>? _nextOpened;

    internal WasiOutputStreamResource(
        IWasiOutputStreamOpener<TStream> opener,
        IWasiOutputStreamCloser<TStream> closer)
    {
        _opener = opener ?? throw new ArgumentNullException();
        _closer = closer ?? throw new ArgumentNullException();
    }

    internal int Writable
    {
        get
        {
            if (_closed)
            {
                throw new ObjectDisposedException(null);
            }

            if (!_opened)
            {
                _ends = _opener.Open();
                _opened = true;
                _nextOpened = s_opened;
                s_opened = this;
            }

            return _ends.Writable;
        }
    }

    internal void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (_opened)
        {
            _closer.Close(_ends);
        }
    }

    // A process entry calls this after Main returns.
    internal static void EndOpened()
    {
        var resource = s_opened;
        s_opened = null;
        while (resource is not null)
        {
            resource.End();
            resource = resource._nextOpened;
        }
    }

    // Closes an open stream at process exit, unless it was already closed, and waits
    // until the host has written everything the program wrote: a host that passes the
    // bytes on asynchronously would otherwise lose what it had accepted but not yet
    // written when the program's export returns (issue 224).
    internal void End() => Close();
}
