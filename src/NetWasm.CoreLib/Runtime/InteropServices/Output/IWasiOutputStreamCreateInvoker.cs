// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

// TStream names the stream (Streams.WasiStreamKinds): an implementation serves one stream only.
internal interface IWasiOutputStreamCreateInvoker<TStream>
{
    // stream.new: the readable end in the low 32 bits, the writable end above.
    long Create();
}
