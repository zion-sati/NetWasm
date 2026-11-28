// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

// TStream names the stream (Streams.WasiStreamKinds): an implementation serves one stream only.
internal interface IWasiOutputStreamHandOffInvoker<TStream>
{
    // Gives the host the readable end and returns the completion future.
    int HandOff(int readable);
}
