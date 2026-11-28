// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

// TStream names the stream (Streams.WasiStreamKinds): an implementation serves one stream only.
internal interface IWasiByteStreamWriteInvoker<TStream>
{
    // An async-lowered stream.write: BLOCKED (-1), or the copied count shifted
    // left by 4 with the copy result in the low 4 bits.
    int Write(int writable, nuint contents, nuint length);
}
