// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Streams;

// TStream names the stream (Streams.WasiStreamKinds): an implementation serves one stream only.
internal interface IWasiByteStreamReadableDropInvoker<TStream>
{
    void Drop(int readable);
}
