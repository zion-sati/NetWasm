// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

// The guest's ends of one write-via-stream: the stream it writes and the
// future that reports when the host has written it all.
internal readonly struct WasiOutputStreamEnds
{
    internal WasiOutputStreamEnds(int writable, int completion)
    {
        if (writable == 0 || completion == 0)
        {
            throw new ArgumentOutOfRangeException();
        }

        Writable = writable;
        Completion = completion;
    }

    internal int Writable { get; }

    internal int Completion { get; }
}
