// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

// TStream names the stream (Streams.WasiStreamKinds): an implementation serves one stream only.
internal interface IWasiResultFutureReadInvoker<TStream>
{
    // An async-lowered future.read into a result<_, error-code> buffer.
    int Read(int readable, nuint result);
}
